namespace TaskFlow.Domain.Jobs;

/// <summary>
/// Raised when a caller asks a job to move to a status the state machine forbids.
/// </summary>
public sealed class InvalidJobTransitionException : InvalidOperationException
{
    public InvalidJobTransitionException(JobStatus from, JobStatus to)
        : base($"Cannot move a job from {from} to {to}.")
    {
        From = from;
        To = to;
    }

    public JobStatus From { get; }

    public JobStatus To { get; }
}
