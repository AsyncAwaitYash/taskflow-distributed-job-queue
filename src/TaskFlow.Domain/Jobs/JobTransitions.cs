namespace TaskFlow.Domain.Jobs;

/// <summary>
/// The only legal job status changes.
/// </summary>
/// <remarks>
/// Pending to Queued is publish. Queued to Processing is a claim.
/// Processing to RetryScheduled waits for <c>NextAttemptAt</c>.
/// RetryScheduled to Queued is the scheduler publishing again.
/// Processing to Failed is a permanent handler failure.
/// Processing to DeadLettered means the retry budget is exhausted.
/// RetryScheduled to DeadLettered means the job must not be published again.
/// Failed or DeadLettered to Queued is a manual retry. Succeeded has no exit.
/// </remarks>
public static class JobTransitions
{
    private static readonly IReadOnlyDictionary<JobStatus, JobStatus[]> Allowed =
        new Dictionary<JobStatus, JobStatus[]>
        {
            [JobStatus.Pending] = [JobStatus.Queued],
            [JobStatus.Queued] = [JobStatus.Processing],
            [JobStatus.Processing] =
            [
                JobStatus.Succeeded,
                JobStatus.RetryScheduled,
                JobStatus.Failed,
                JobStatus.DeadLettered
            ],
            [JobStatus.RetryScheduled] = [JobStatus.Queued, JobStatus.DeadLettered],
            [JobStatus.Failed] = [JobStatus.Queued],
            [JobStatus.DeadLettered] = [JobStatus.Queued],
            [JobStatus.Succeeded] = []
        };

    public static bool CanTransition(JobStatus from, JobStatus to)
    {
        return Allowed.TryGetValue(from, out JobStatus[]? targets)
            && targets.Contains(to);
    }

    public static void Ensure(JobStatus from, JobStatus to)
    {
        if (!CanTransition(from, to))
        {
            throw new InvalidJobTransitionException(from, to);
        }
    }
}
