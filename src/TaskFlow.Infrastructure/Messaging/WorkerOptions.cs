using TaskFlow.Domain.Jobs;

namespace TaskFlow.Infrastructure.Messaging;

public sealed class WorkerOptions
{
    public const string SectionName = "TaskFlow:Worker";

    /// <summary>
    /// Unacknowledged messages RabbitMQ may hand this worker at once. Small, so one worker does not hoard jobs.
    /// </summary>
    public ushort PrefetchCount { get; set; } = 1;

    /// <summary>
    /// Recorded on each attempt. Defaults to the machine name and process id.
    /// </summary>
    public string? WorkerId { get; set; }

    /// <summary>
    /// Wait before handing a message back while SQL Server is unavailable.
    /// </summary>
    public TimeSpan DatabaseRetryDelay { get; set; } = TimeSpan.FromSeconds(5);

    /// <summary>
    /// Wait between attempts to reach RabbitMQ at startup.
    /// </summary>
    public TimeSpan ConnectRetryDelay { get; set; } = TimeSpan.FromSeconds(5);

    internal string ResolveWorkerId()
    {
        string id = string.IsNullOrWhiteSpace(WorkerId)
            ? $"{Environment.MachineName}-{Environment.ProcessId}"
            : WorkerId.Trim();
        return id.Length <= JobLimits.MaxWorkerIdLength ? id : id[..JobLimits.MaxWorkerIdLength];
    }
}
