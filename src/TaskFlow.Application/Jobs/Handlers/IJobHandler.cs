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

public sealed record JobHandlerResult
{
    private JobHandlerResult(bool succeeded, string? errorType, string? errorMessage)
    {
        Succeeded = succeeded;
        ErrorType = errorType;
        ErrorMessage = errorMessage;
    }

    public bool Succeeded { get; }

    public string? ErrorType { get; }

    public string? ErrorMessage { get; }

    public static JobHandlerResult Success()
    {
        return new JobHandlerResult(true, null, null);
    }

    /// <summary>
    /// Trying again would fail the same way. The job moves to Failed.
    /// </summary>
    public static JobHandlerResult PermanentFailure(string errorType, string errorMessage)
    {
        return new JobHandlerResult(false, errorType, errorMessage);
    }
}
