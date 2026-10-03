namespace TaskFlow.Application.Jobs.Handlers;

/// <summary>
/// The business step for one job type. Handlers know nothing about RabbitMQ or SQL Server.
/// A handler may run more than once for the same job, so its side effects must tolerate a repeat.
/// </summary>
public interface IJobHandler
{
    string Type { get; }

    Task<JobHandlerResult> HandleAsync(JobContext context, CancellationToken cancellationToken);
}

public sealed record JobContext(Guid JobId, string Type, string Payload, int AttemptNumber, string CorrelationId);

public enum JobHandlerOutcome
{
    Succeeded,
    RetryableFailure,
    PermanentFailure
}

public sealed record JobHandlerResult
{
    private JobHandlerResult(JobHandlerOutcome outcome, string? errorType, string? errorMessage)
    {
        Outcome = outcome;
        ErrorType = errorType;
        ErrorMessage = errorMessage;
    }

    public JobHandlerOutcome Outcome { get; }

    public bool Succeeded => Outcome == JobHandlerOutcome.Succeeded;

    public string? ErrorType { get; }

    public string? ErrorMessage { get; }

    public static JobHandlerResult Success()
    {
        return new JobHandlerResult(JobHandlerOutcome.Succeeded, null, null);
    }

    /// <summary>
    /// Worth another try. The worker schedules it; the handler does not choose the delay.
    /// </summary>
    public static JobHandlerResult RetryableFailure(string errorType, string errorMessage)
    {
        return new JobHandlerResult(JobHandlerOutcome.RetryableFailure, errorType, errorMessage);
    }

    /// <summary>
    /// Trying again would fail the same way. The job moves to Failed.
    /// </summary>
    public static JobHandlerResult PermanentFailure(string errorType, string errorMessage)
    {
        return new JobHandlerResult(JobHandlerOutcome.PermanentFailure, errorType, errorMessage);
    }
}
