using TaskFlow.Application.Jobs.Handlers;
using TaskFlow.Domain.Jobs;

namespace TaskFlow.Application.Jobs;

public enum JobProcessingOutcome
{
    Succeeded,
    Failed,
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
    private readonly TimeProvider _clock;

    public JobProcessor(IJobRepository repository, JobHandlerRegistry handlers, TimeProvider clock)
    {
        _repository = repository;
        _handlers = handlers;
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
        if (result.Succeeded)
        {
            job.CompleteSuccessfully(completedAt);
        }
        else
        {
            job.FailPermanently(result.ErrorType!, result.ErrorMessage!, completedAt);
        }

        await _repository.SaveChangesAsync(cancellationToken);
        return result.Succeeded ? JobProcessingOutcome.Succeeded : JobProcessingOutcome.Failed;
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
            // Until the Phase 4 classifier exists, an unexpected error is not retried.
            // The message text is not stored because it can echo payload values.
            string errorType = exception.GetType().Name;
            return JobHandlerResult.PermanentFailure(errorType, $"The handler threw {errorType}.");
        }
    }
}
