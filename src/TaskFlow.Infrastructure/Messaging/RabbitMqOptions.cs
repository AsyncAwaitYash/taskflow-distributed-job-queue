namespace TaskFlow.Infrastructure.Messaging;

public sealed class RabbitMqOptions
{
    public const string SectionName = "TaskFlow:RabbitMq";

    /// <summary>
    /// Upper bound for connecting, declaring the topology, and waiting for the broker confirm.
    /// </summary>
    public TimeSpan PublishTimeout { get; set; } = TimeSpan.FromSeconds(5);
}
