using RabbitMQ.Client;

namespace TaskFlow.Infrastructure.Messaging;

public static class RabbitMqTopology
{
    public const string Exchange = "taskflow.jobs";
    public const string ProcessQueue = "taskflow.jobs.process";
    public const string ProcessRoutingKey = "job.process";

    /// <summary>
    /// Declares the exchange, the queue, and the binding. Safe to call again with the same arguments.
    /// </summary>
    public static async Task DeclareAsync(IChannel channel, CancellationToken cancellationToken)
    {
        await channel.ExchangeDeclareAsync(
            Exchange,
            ExchangeType.Direct,
            durable: true,
            autoDelete: false,
            arguments: null,
            cancellationToken: cancellationToken);

        await channel.QueueDeclareAsync(
            ProcessQueue,
            durable: true,
            exclusive: false,
            autoDelete: false,
            arguments: null,
            cancellationToken: cancellationToken);

        await channel.QueueBindAsync(
            ProcessQueue,
            Exchange,
            ProcessRoutingKey,
            arguments: null,
            cancellationToken: cancellationToken);
    }
}
