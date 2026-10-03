namespace TaskFlow.Domain.Jobs;

/// <summary>
/// One try of a job. A job has many attempts. An attempt is not a RabbitMQ message.
/// </summary>
public sealed class JobAttempt
{
    private JobAttempt(Guid id, Guid jobId, int attemptNumber, string workerId, DateTimeOffset startedAt)
    {
        Id = id;
        JobId = jobId;
        AttemptNumber = attemptNumber;
        WorkerId = workerId;
        StartedAt = startedAt;
    }

    public Guid Id { get; }

    public Guid JobId { get; }

    public int AttemptNumber { get; }

    public DateTimeOffset StartedAt { get; }

    public DateTimeOffset? CompletedAt { get; private set; }

    public JobAttemptOutcome? Outcome { get; private set; }

    public string? ErrorType { get; private set; }

    public string? ErrorMessage { get; private set; }

    public TimeSpan? Duration { get; private set; }

    public string WorkerId { get; }

    public bool IsOpen => Outcome is null;

    internal static JobAttempt Start(Guid jobId, int attemptNumber, string workerId, DateTimeOffset startedAt)
    {
        return new JobAttempt(Guid.NewGuid(), jobId, attemptNumber, workerId, startedAt);
    }

    internal void Complete(
        JobAttemptOutcome outcome,
        string? errorType,
        string? errorMessage,
        DateTimeOffset completedAt)
    {
        if (!IsOpen)
        {
            throw new InvalidOperationException("This attempt is already complete.");
        }

        if (completedAt < StartedAt)
        {
            throw new ArgumentOutOfRangeException(
                nameof(completedAt),
                "Completion time cannot be earlier than the start time.");
        }

        string? normalizedType = null;
        string? normalizedMessage = null;
        if (outcome == JobAttemptOutcome.Succeeded)
        {
            if (errorType is not null || errorMessage is not null)
            {
                throw new ArgumentException("A successful attempt does not record an error.");
            }
        }
        else
        {
            normalizedType = DomainGuard.Required(errorType, nameof(errorType), JobLimits.MaxErrorTypeLength);
            normalizedMessage = DomainGuard.Required(errorMessage, nameof(errorMessage), JobLimits.MaxErrorMessageLength);
        }

        ErrorType = normalizedType;
        ErrorMessage = normalizedMessage;
        CompletedAt = completedAt;
        Duration = completedAt - StartedAt;
        Outcome = outcome;
    }
}
