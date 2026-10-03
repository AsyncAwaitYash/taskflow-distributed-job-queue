namespace TaskFlow.Application.Jobs;

/// <summary>
/// The job row was committed as Pending, but the publish was not confirmed.
/// </summary>
public sealed class JobNotQueuedException : Exception
{
    public JobNotQueuedException(Guid jobId, Exception innerException)
        : base("The job was stored but not queued.", innerException)
    {
        JobId = jobId;
    }

    public Guid JobId { get; }
}
