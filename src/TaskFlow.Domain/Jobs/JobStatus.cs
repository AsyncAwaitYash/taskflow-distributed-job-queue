namespace TaskFlow.Domain.Jobs;

/// <summary>
/// Lifecycle of one job. Workers may start an attempt only from <see cref="Queued"/>.
/// </summary>
public enum JobStatus
{
    Pending = 0,
    Queued = 1,
    Processing = 2,
    Succeeded = 3,
    RetryScheduled = 4,
    Failed = 5,
    DeadLettered = 6
}
