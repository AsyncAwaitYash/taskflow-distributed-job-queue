namespace TaskFlow.Domain.Jobs;

/// <summary>
/// Result of a single attempt. Dead-lettering is a job status, not an attempt outcome.
/// </summary>
public enum JobAttemptOutcome
{
    Succeeded = 0,
    RetryableFailure = 1,
    PermanentFailure = 2
}
