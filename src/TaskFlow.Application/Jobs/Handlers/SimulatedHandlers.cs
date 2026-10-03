using System.Text.Json;

namespace TaskFlow.Application.Jobs.Handlers;

/// <summary>
/// Simulated. Checks for a recipient and sends nothing. No SMTP, and the recipient is not logged.
/// </summary>
public sealed class EmailSendHandler : IJobHandler
{
    public string Type => "email.send";

    public Task<JobHandlerResult> HandleAsync(JobContext context, CancellationToken cancellationToken)
    {
        JsonElement payload = JobPayload.Parse(context.Payload);
        if (!payload.TryGetProperty("to", out JsonElement to)
            || to.ValueKind != JsonValueKind.String
            || string.IsNullOrWhiteSpace(to.GetString()))
        {
            return Task.FromResult(JobHandlerResult.PermanentFailure(
                JobPayload.InvalidPayload,
                "email.send needs a non-empty 'to' string."));
        }

        return Task.FromResult(JobHandlerResult.Success());
    }
}

/// <summary>
/// Simulated. Produces nothing.
/// </summary>
public sealed class ReportGenerateHandler : IJobHandler
{
    public string Type => "report.generate";

    public Task<JobHandlerResult> HandleAsync(JobContext context, CancellationToken cancellationToken)
    {
        return Task.FromResult(JobHandlerResult.Success());
    }
}

/// <summary>
/// Simulated. Produces nothing.
/// </summary>
public sealed class DataProcessHandler : IJobHandler
{
    public string Type => "data.process";

    public Task<JobHandlerResult> HandleAsync(JobContext context, CancellationToken cancellationToken)
    {
        return Task.FromResult(JobHandlerResult.Success());
    }
}
