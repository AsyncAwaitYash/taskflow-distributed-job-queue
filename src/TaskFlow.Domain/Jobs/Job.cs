namespace TaskFlow.Domain.Jobs;

/// <summary>
/// The durable unit of work. Status changes only through the methods on this type.
/// </summary>
public sealed class Job
{
    private readonly List<JobAttempt> _attempts = [];

    private Job(
        Guid id,
        string type,
        string payload,
        int maxAttempts,
        string correlationId,
        DateTimeOffset createdAt)
    {
        Id = id;
        Type = type;
        Payload = payload;
        MaxAttempts = maxAttempts;
        CorrelationId = correlationId;
        CreatedAt = createdAt;
        UpdatedAt = createdAt;
        Status = JobStatus.Pending;
    }

    public Guid Id { get; }

    public string Type { get; }

    public string Payload { get; }

    public JobStatus Status { get; private set; }

    public int AttemptCount { get; private set; }

    public int MaxAttempts { get; }

    public DateTimeOffset? NextAttemptAt { get; private set; }

    public DateTimeOffset? LastStartedAt { get; private set; }

    public DateTimeOffset? LastCompletedAt { get; private set; }

    public string? LastError { get; private set; }

    public DateTimeOffset CreatedAt { get; }

    public DateTimeOffset UpdatedAt { get; private set; }

    public string CorrelationId { get; }

    public IReadOnlyList<JobAttempt> Attempts => _attempts;

    public static Job Create(
        string type,
        string payload,
        int maxAttempts,
        DateTimeOffset createdAt,
        string? correlationId = null,
        Guid? id = null)
    {
        if (maxAttempts < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(maxAttempts), "Max attempts must be at least 1.");
        }

        if (payload is null)
        {
            throw new ArgumentNullException(nameof(payload));
        }

        if (string.IsNullOrWhiteSpace(payload))
        {
            throw new ArgumentException("Payload is required.", nameof(payload));
        }

        if (payload.Length > JobLimits.MaxPayloadLength)
        {
            throw new ArgumentException(
                $"Payload must be at most {JobLimits.MaxPayloadLength} characters.",
                nameof(payload));
        }

        string normalizedType = DomainGuard.Required(type, nameof(type), JobLimits.MaxTypeLength);
        string normalizedCorrelationId = string.IsNullOrWhiteSpace(correlationId)
            ? Guid.NewGuid().ToString("N")
            : DomainGuard.Required(correlationId, nameof(correlationId), JobLimits.MaxCorrelationIdLength);

        return new Job(id ?? Guid.NewGuid(), normalizedType, payload, maxAttempts, normalizedCorrelationId, createdAt);
    }

    public void MarkQueued(DateTimeOffset at)
    {
        EnsureClock(at);
        if (Status is JobStatus.Failed or JobStatus.DeadLettered)
        {
            throw new InvalidOperationException(
                $"MarkQueued cannot republish a {Status} job. Call RetryManually.");
        }

        JobTransitions.Ensure(Status, JobStatus.Queued);
        Status = JobStatus.Queued;
        NextAttemptAt = null;
        UpdatedAt = at;
    }

    public JobAttempt StartProcessing(string workerId, DateTimeOffset startedAt)
    {
        string normalizedWorkerId = DomainGuard.Required(workerId, nameof(workerId), JobLimits.MaxWorkerIdLength);
        EnsureClock(startedAt);
        JobTransitions.Ensure(Status, JobStatus.Processing);

        AttemptCount++;
        JobAttempt attempt = JobAttempt.Start(Id, AttemptCount, normalizedWorkerId, startedAt);
        _attempts.Add(attempt);
        Status = JobStatus.Processing;
        LastStartedAt = startedAt;
        NextAttemptAt = null;
        UpdatedAt = startedAt;
        return attempt;
    }

    public void CompleteSuccessfully(DateTimeOffset completedAt)
    {
        EnsureClock(completedAt);
        JobTransitions.Ensure(Status, JobStatus.Succeeded);
        JobAttempt attempt = OpenAttempt();
        attempt.Complete(JobAttemptOutcome.Succeeded, null, null, completedAt);
        Status = JobStatus.Succeeded;
        LastError = null;
        NextAttemptAt = null;
        LastCompletedAt = completedAt;
        UpdatedAt = completedAt;
    }

    public void FailPermanently(string errorType, string errorMessage, DateTimeOffset completedAt)
    {
        EnsureClock(completedAt);
        JobTransitions.Ensure(Status, JobStatus.Failed);
        JobAttempt attempt = OpenAttempt();
        attempt.Complete(JobAttemptOutcome.PermanentFailure, errorType, errorMessage, completedAt);
        Status = JobStatus.Failed;
        LastError = attempt.ErrorMessage;
        NextAttemptAt = null;
        LastCompletedAt = completedAt;
        UpdatedAt = completedAt;
    }

    /// <summary>
    /// Records a retryable failure. Exhausting <see cref="MaxAttempts"/> dead-letters the job
    /// instead of scheduling another try. The delay itself is chosen by the caller.
    /// </summary>
    public void RecordRetryableFailure(
        string errorType,
        string errorMessage,
        DateTimeOffset completedAt,
        DateTimeOffset? nextAttemptAt)
    {
        EnsureClock(completedAt);
        bool exhausted = AttemptCount >= MaxAttempts;
        JobStatus target = exhausted ? JobStatus.DeadLettered : JobStatus.RetryScheduled;
        if (Status != JobStatus.Processing)
        {
            throw new InvalidJobTransitionException(Status, target);
        }

        if (!exhausted && (nextAttemptAt is null || nextAttemptAt.Value < completedAt))
        {
            throw new ArgumentException(
                "A retry needs a next attempt time at or after the failure time.",
                nameof(nextAttemptAt));
        }

        JobTransitions.Ensure(Status, target);
        JobAttempt attempt = OpenAttempt();
        attempt.Complete(JobAttemptOutcome.RetryableFailure, errorType, errorMessage, completedAt);
        Status = target;
        LastError = attempt.ErrorMessage;
        NextAttemptAt = exhausted ? null : nextAttemptAt;
        LastCompletedAt = completedAt;
        UpdatedAt = completedAt;
    }

    /// <summary>
    /// Gives up on a job that is already waiting to be published again.
    /// </summary>
    public void DeadLetter(string reason, DateTimeOffset at)
    {
        string normalizedReason = DomainGuard.Required(reason, nameof(reason), JobLimits.MaxErrorMessageLength);
        EnsureClock(at);
        if (Status == JobStatus.Processing)
        {
            throw new InvalidOperationException(
                "An in-progress attempt is dead-lettered by RecordRetryableFailure when the budget is exhausted.");
        }

        JobTransitions.Ensure(Status, JobStatus.DeadLettered);
        Status = JobStatus.DeadLettered;
        LastError = normalizedReason;
        NextAttemptAt = null;
        UpdatedAt = at;
    }

    /// <summary>
    /// Puts a failed or dead-lettered job back on the queue. Does not change the attempt budget.
    /// </summary>
    public void RetryManually(DateTimeOffset at)
    {
        EnsureClock(at);
        if (Status is JobStatus.Pending or JobStatus.RetryScheduled)
        {
            throw new InvalidOperationException(
                $"RetryManually does not publish a {Status} job. Call MarkQueued.");
        }

        JobTransitions.Ensure(Status, JobStatus.Queued);
        Status = JobStatus.Queued;
        NextAttemptAt = null;
        UpdatedAt = at;
    }

    private JobAttempt OpenAttempt()
    {
        JobAttempt? attempt = _attempts.LastOrDefault(candidate => candidate.IsOpen);
        if (attempt is null)
        {
            throw new InvalidOperationException("A processing job is missing its open attempt.");
        }

        return attempt;
    }

    private void EnsureClock(DateTimeOffset at)
    {
        if (at < UpdatedAt)
        {
            throw new ArgumentOutOfRangeException(nameof(at), "The event time cannot be earlier than the job's last update.");
        }
    }
}
