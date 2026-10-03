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
/// <summary>
/// Returns a retryable failure for the first <c>failTimes</c> attempts (0 to <see cref="MaxFailTimes"/>), then succeeds.
/// The worker chooses the delay.
/// </summary>
public sealed class DemoTransientFailureHandler : IJobHandler
{
    public const int MaxFailTimes = 20;

    public string Type => "demo.transient-failure";

    public Task<JobHandlerResult> HandleAsync(JobContext context, CancellationToken cancellationToken)
    {
        JsonElement payload = JobPayload.Parse(context.Payload);
        if (!payload.TryGetProperty("failTimes", out JsonElement failTimes)
            || failTimes.ValueKind != JsonValueKind.Number
            || !failTimes.TryGetInt32(out int value)
            || value < 0
            || value > MaxFailTimes)
        {
            return Task.FromResult(JobHandlerResult.PermanentFailure(
                JobPayload.InvalidPayload,
                $"demo.transient-failure needs a whole number 'failTimes' from 0 to {MaxFailTimes}."));
        }

        if (context.AttemptNumber <= value)
        {
            return Task.FromResult(JobHandlerResult.RetryableFailure(
                "DemoTransientFailure",
                $"demo.transient-failure fails its first {value} attempts."));
        }

        return Task.FromResult(JobHandlerResult.Success());
    }
}

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
