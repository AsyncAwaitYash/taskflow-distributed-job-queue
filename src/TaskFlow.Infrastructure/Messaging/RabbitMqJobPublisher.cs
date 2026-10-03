using System.Net.Sockets;
using System.Text.Json;

using TaskFlow.Application.Jobs;

using Microsoft.Extensions.Options;

using RabbitMQ.Client;
using RabbitMQ.Client.Exceptions;

namespace TaskFlow.Infrastructure.Messaging;

internal sealed class RabbitMqJobPublisher : IJobPublisher
{
    private static readonly JsonSerializerOptions BodyOptions = new(JsonSerializerDefaults.Web);

    private static readonly CreateChannelOptions ConfirmedChannel = new(
        publisherConfirmationsEnabled: true,
        publisherConfirmationTrackingEnabled: true);

    private readonly RabbitMqConnectionProvider _connections;
    private readonly TimeSpan _timeout;

    public RabbitMqJobPublisher(RabbitMqConnectionProvider connections, IOptions<RabbitMqOptions> options)
    {
        _connections = connections;
        _timeout = options.Value.PublishTimeout;
    }

    public async Task PublishAsync(JobMessage message, CancellationToken cancellationToken)
    {
        using CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(_timeout);

        try
        {
            IConnection connection = await _connections.GetConnectionAsync(timeout.Token);
            await using IChannel channel = await connection.CreateChannelAsync(ConfirmedChannel, timeout.Token);

            BasicProperties properties = new()
            {
                Persistent = true,
                ContentType = "application/json",
                MessageId = message.JobId.ToString(),
                CorrelationId = message.CorrelationId,
                Type = message.Type
            };

            byte[] body = JsonSerializer.SerializeToUtf8Bytes(message, BodyOptions);

            // With confirmation tracking on, this completes only after the broker acks,
            // and throws if the broker nacks or returns the message as unroutable.
            await channel.BasicPublishAsync(
                RabbitMqTopology.Exchange,
                RabbitMqTopology.ProcessRoutingKey,
                mandatory: true,
                properties,
                body,
                timeout.Token);
        }
        catch (OperationCanceledException exception) when (!cancellationToken.IsCancellationRequested)
        {
            throw new JobPublishFailedException("RabbitMQ did not confirm the publish in time.", exception);
        }
        catch (PublishException exception)
        {
            throw new JobPublishFailedException(
                exception.IsReturn ? "RabbitMQ returned the message as unroutable." : "RabbitMQ rejected the publish.",
                exception);
        }
        catch (Exception exception) when (exception is RabbitMQClientException or IOException or SocketException or TimeoutException)
        {
            throw new JobPublishFailedException("RabbitMQ is unreachable.", exception);
        }
    }
}
