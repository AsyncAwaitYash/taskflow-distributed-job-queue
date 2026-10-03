using System.Text.Json;

namespace TaskFlow.Application.Jobs.Handlers;

public sealed class DemoSuccessHandler : IJobHandler
{
    public string Type => "demo.success";

    public Task<JobHandlerResult> HandleAsync(JobContext context, CancellationToken cancellationToken)
    {
        return Task.FromResult(JobHandlerResult.Success());
    }
}

public sealed class DemoPermanentFailureHandler : IJobHandler
{
    public string Type => "demo.permanent-failure";

    public Task<JobHandlerResult> HandleAsync(JobContext context, CancellationToken cancellationToken)
    {
        return Task.FromResult(JobHandlerResult.PermanentFailure(
            "DemoPermanentFailure",
            "demo.permanent-failure always fails."));
    }
}

/// <summary>
/// Waits for <c>seconds</c> (0 to <see cref="MaxSeconds"/>) from the payload, then succeeds.
/// </summary>
public sealed class DemoSlowHandler : IJobHandler
{
    public const int MaxSeconds = 30;

    private readonly TimeProvider _clock;

    public DemoSlowHandler(TimeProvider clock)
    {
        _clock = clock;
    }

    public string Type => "demo.slow";

    public async Task<JobHandlerResult> HandleAsync(JobContext context, CancellationToken cancellationToken)
    {
        JsonElement payload = JobPayload.Parse(context.Payload);
        if (!payload.TryGetProperty("seconds", out JsonElement seconds)
            || seconds.ValueKind != JsonValueKind.Number
            || !seconds.TryGetInt32(out int value)
            || value < 0
            || value > MaxSeconds)
        {
            return JobHandlerResult.PermanentFailure(
                JobPayload.InvalidPayload,
                $"demo.slow needs a whole number 'seconds' from 0 to {MaxSeconds}.");
        }

        await Task.Delay(TimeSpan.FromSeconds(value), _clock, cancellationToken);
        return JobHandlerResult.Success();
    }
}
