using System.Diagnostics;
using System.Text.Json;

using TaskFlow.Application.Jobs;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace TaskFlow.Infrastructure.Messaging;

/// <summary>
/// Consumes <see cref="RabbitMqTopology.ProcessQueue"/> with manual acknowledgement.
/// A message is acked only after <see cref="JobProcessor"/> has saved the outcome.
/// </summary>
internal sealed class RabbitMqJobConsumer : BackgroundService
{
    private static readonly JsonSerializerOptions BodyOptions = new(JsonSerializerDefaults.Web);

    private readonly RabbitMqConnectionProvider _connections;
    private readonly IServiceScopeFactory _scopes;
    private readonly ILogger<RabbitMqJobConsumer> _logger;
    private readonly WorkerOptions _options;
    private readonly string _workerId;
    private readonly SemaphoreSlim _inFlight = new(1, 1);
    private CancellationToken _stopping;

    public RabbitMqJobConsumer(
        RabbitMqConnectionProvider connections,
        IServiceScopeFactory scopes,
        IOptions<WorkerOptions> options,
        ILogger<RabbitMqJobConsumer> logger)
    {
        _connections = connections;
        _scopes = scopes;
        _logger = logger;
        _options = options.Value;
        _workerId = _options.ResolveWorkerId();
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _stopping = stoppingToken;

        IConnection? connection = await ConnectAsync(stoppingToken);
        if (connection is null)
        {
            return;
        }

        await using IChannel channel = await connection.CreateChannelAsync(cancellationToken: stoppingToken);
        await channel.BasicQosAsync(0, _options.PrefetchCount, global: false, stoppingToken);

        AsyncEventingBasicConsumer consumer = new(channel);
        consumer.ReceivedAsync += (_, delivery) => HandleDeliveryAsync(channel, delivery);
        string consumerTag = await channel.BasicConsumeAsync(
            RabbitMqTopology.ProcessQueue,
            autoAck: false,
            consumer,
            stoppingToken);

        _logger.LogInformation(
            "TaskFlow worker consuming. WorkerId={WorkerId} Queue={Queue} PrefetchCount={PrefetchCount}",
            _workerId,
            RabbitMqTopology.ProcessQueue,
            _options.PrefetchCount);

        try
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }

        _logger.LogInformation("TaskFlow worker stopping. WorkerId={WorkerId}", _workerId);
        try
        {
            await channel.BasicCancelAsync(consumerTag, noWait: false, CancellationToken.None);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            _logger.LogWarning("Consumer cancel failed. ExceptionType={ExceptionType}", exception.GetType().Name);
        }

        // Let the in-flight message reach its ack. Anything still unacked is requeued when the channel closes.
        await _inFlight.WaitAsync(CancellationToken.None);
        _inFlight.Release();
    }

    public override void Dispose()
    {
        _inFlight.Dispose();
        base.Dispose();
    }

    private async Task<IConnection?> ConnectAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                return await _connections.GetConnectionAsync(stoppingToken);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                _logger.LogWarning(
                    "RabbitMQ is unreachable. Retrying. ExceptionType={ExceptionType} RetryDelay={RetryDelay}",
                    exception.GetType().Name,
                    _options.ConnectRetryDelay);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return null;
            }

            try
            {
                await Task.Delay(_options.ConnectRetryDelay, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                return null;
            }
        }

        return null;
    }

    private async Task HandleDeliveryAsync(IChannel channel, BasicDeliverEventArgs delivery)
    {
        await _inFlight.WaitAsync(CancellationToken.None);
        try
        {
            await HandleAsync(channel, delivery);
        }
        finally
        {
            _inFlight.Release();
        }
    }

    private async Task HandleAsync(IChannel channel, BasicDeliverEventArgs delivery)
    {
        JobMessage? message = TryRead(delivery.Body);
        if (message is null)
        {
            _logger.LogWarning(
                "Rejected an unreadable message. DeliveryTag={DeliveryTag} WorkerId={WorkerId}",
                delivery.DeliveryTag,
                _workerId);
            await SettleAsync(() => channel.BasicRejectAsync(delivery.DeliveryTag, requeue: false), delivery.DeliveryTag);
            return;
        }

        Stopwatch timer = Stopwatch.StartNew();
        try
        {
            await using AsyncServiceScope scope = _scopes.CreateAsyncScope();
            JobProcessor processor = scope.ServiceProvider.GetRequiredService<JobProcessor>();

            // The job runs to completion even during shutdown, so its outcome is saved before the ack.
            JobProcessingOutcome outcome = await processor.ProcessAsync(message.JobId, _workerId, CancellationToken.None);

            _logger.LogInformation(
                "Processed delivery. JobId={JobId} Outcome={Outcome} Redelivered={Redelivered} WorkerId={WorkerId} CorrelationId={CorrelationId} DurationMs={DurationMs}",
                message.JobId,
                outcome,
                delivery.Redelivered,
                _workerId,
                message.CorrelationId,
                timer.ElapsedMilliseconds);

            await SettleAsync(() => channel.BasicAckAsync(delivery.DeliveryTag, multiple: false), delivery.DeliveryTag);
        }
        catch (JobDatabaseUnavailableException exception)
        {
            _logger.LogWarning(
                "SQL Server is unavailable. Returning the message after a delay. JobId={JobId} WorkerId={WorkerId} ExceptionType={ExceptionType} RetryDelay={RetryDelay}",
                message.JobId,
                _workerId,
                exception.InnerException?.GetType().Name,
                _options.DatabaseRetryDelay);

            try
            {
                await Task.Delay(_options.DatabaseRetryDelay, _stopping);
            }
            catch (OperationCanceledException)
            {
            }

            await SettleAsync(() => channel.BasicNackAsync(delivery.DeliveryTag, multiple: false, requeue: true), delivery.DeliveryTag);
        }
        catch (Exception exception)
        {
            // Requeueing an error nobody understands would loop forever. The row keeps whatever was last saved.
            _logger.LogError(
                "Unexpected error while processing. Rejecting the message. JobId={JobId} WorkerId={WorkerId} ExceptionType={ExceptionType}",
                message.JobId,
                _workerId,
                exception.GetType().Name);
            await SettleAsync(() => channel.BasicRejectAsync(delivery.DeliveryTag, requeue: false), delivery.DeliveryTag);
        }
    }

    private async Task SettleAsync(Func<ValueTask> settle, ulong deliveryTag)
    {
        try
        {
            await settle();
        }
        catch (Exception exception)
        {
            // The channel is gone. RabbitMQ redelivers the message, and the processor skips a finished job.
            _logger.LogWarning(
                "Could not settle the delivery. DeliveryTag={DeliveryTag} ExceptionType={ExceptionType}",
                deliveryTag,
                exception.GetType().Name);
        }
    }

    private static JobMessage? TryRead(ReadOnlyMemory<byte> body)
    {
        try
        {
            JobMessage? message = JsonSerializer.Deserialize<JobMessage>(body.Span, BodyOptions);
            return message is { JobId: var id, Type: not null, CorrelationId: not null } && id != Guid.Empty ? message : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
