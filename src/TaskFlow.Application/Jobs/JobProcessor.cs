using TaskFlow.Application.Jobs.Handlers;
using TaskFlow.Domain.Jobs;

namespace TaskFlow.Application.Jobs;

public enum JobProcessingOutcome
{
    Succeeded,
    Failed,
    RetryScheduled,
    DeadLettered,
    SkippedNotFound,
    SkippedNotQueued
}

/// <summary>
/// Runs one delivery of a job. Every return value means the outcome is saved and the message can be acked.
/// A <see cref="JobDatabaseUnavailableException"/> means nothing new is known to be saved, so the message must not be acked.
/// </summary>
public sealed class JobProcessor
{
    public const string NoHandlerRegistered = "NoHandlerRegistered";

    private readonly IJobRepository _repository;
    private readonly JobHandlerRegistry _handlers;
    private readonly RetryBackoffPolicy _backoff;
    private readonly TimeProvider _clock;

    public JobProcessor(
        IJobRepository repository,
        JobHandlerRegistry handlers,
        RetryBackoffPolicy backoff,
        TimeProvider clock)
    {
        _repository = repository;
        _handlers = handlers;
        _backoff = backoff;
        _clock = clock;
    }

    public async Task<JobProcessingOutcome> ProcessAsync(Guid jobId, string workerId, CancellationToken cancellationToken)
    {
        Job? job = await _repository.GetAsync(jobId, cancellationToken);
        if (job is null)
        {
            return JobProcessingOutcome.SkippedNotFound;
        }

        // A message exists, so the publish happened even if the Queued save did not.
        if (job.Status == JobStatus.Pending)
        {
            job.MarkQueued(_clock.GetUtcNow());
        }

        if (job.Status != JobStatus.Queued)
        {
            return JobProcessingOutcome.SkippedNotQueued;
        }

        JobAttempt attempt = job.StartProcessing(workerId, _clock.GetUtcNow());
        await _repository.SaveChangesAsync(cancellationToken);

        JobHandlerResult result = await RunHandlerAsync(job, attempt, cancellationToken);

        DateTimeOffset completedAt = _clock.GetUtcNow();
        JobProcessingOutcome outcome = Apply(job, attempt, result, completedAt);
        await _repository.SaveChangesAsync(cancellationToken);
        return outcome;
    }

    private JobProcessingOutcome Apply(Job job, JobAttempt attempt, JobHandlerResult result, DateTimeOffset completedAt)
    {
        switch (result.Outcome)
        {
            case JobHandlerOutcome.Succeeded:
                job.CompleteSuccessfully(completedAt);
                return JobProcessingOutcome.Succeeded;

            case JobHandlerOutcome.PermanentFailure:
                job.FailPermanently(result.ErrorType!, result.ErrorMessage!, completedAt);
                return JobProcessingOutcome.Failed;

            case JobHandlerOutcome.RetryableFailure:
                // RecordRetryableFailure dead-letters instead when the attempt budget is spent.
                DateTimeOffset nextAttemptAt = completedAt + _backoff.GetDelay(attempt.AttemptNumber);
                job.RecordRetryableFailure(result.ErrorType!, result.ErrorMessage!, completedAt, nextAttemptAt);
                return job.Status == JobStatus.DeadLettered
                    ? JobProcessingOutcome.DeadLettered
                    : JobProcessingOutcome.RetryScheduled;

            default:
                throw new InvalidOperationException($"Unhandled handler outcome {result.Outcome}.");
        }
    }

    private async Task<JobHandlerResult> RunHandlerAsync(Job job, JobAttempt attempt, CancellationToken cancellationToken)
    {
        IJobHandler? handler = _handlers.Find(job.Type);
        if (handler is null)
        {
            return JobHandlerResult.PermanentFailure(NoHandlerRegistered, $"No handler is registered for '{job.Type}'.");
        }

        JobContext context = new(job.Id, job.Type, job.Payload, attempt.AttemptNumber, job.CorrelationId);
        try
        {
            return await handler.HandleAsync(context, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            // The message text is not stored because it can echo payload values.
            string errorType = exception.GetType().Name;
            string message = $"The handler threw {errorType}.";
            return JobFailureClassifier.IsPermanent(exception)
                ? JobHandlerResult.PermanentFailure(errorType, message)
                : JobHandlerResult.RetryableFailure(errorType, message);
        }
    }
}
