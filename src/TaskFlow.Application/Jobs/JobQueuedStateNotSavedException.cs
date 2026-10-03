namespace TaskFlow.Application.Jobs;

/// <summary>
/// The broker confirmed the message, but SQL Server did not accept the Queued status.
/// The row stays Pending while a message for it sits in the queue.
/// </summary>
public sealed class JobQueuedStateNotSavedException : Exception
{
    public JobQueuedStateNotSavedException(Guid jobId, Exception innerException)
        : base("The job was published but its Queued status was not saved.", innerException)
    {
        JobId = jobId;
    }

    public Guid JobId { get; }
}
