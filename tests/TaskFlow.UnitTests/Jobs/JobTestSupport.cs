using TaskFlow.Application.Jobs;
using TaskFlow.Application.Jobs.Handlers;

using Microsoft.Extensions.Options;

namespace TaskFlow.UnitTests.Jobs;

internal sealed class FixedTimeProvider : TimeProvider
{
    private readonly DateTimeOffset _now;

    public FixedTimeProvider(DateTimeOffset now)
    {
        _now = now;
    }

    public override DateTimeOffset GetUtcNow()
    {
        return _now;
    }
}

internal static class JobHandlerSet
{
    public static JobHandlerRegistry Registry(TimeProvider clock)
    {
        return new JobHandlerRegistry(
        [
            new DemoSuccessHandler(),
            new DemoPermanentFailureHandler(),
            new DemoTransientFailureHandler(),
            new DemoSlowHandler(clock),
            new EmailSendHandler(),
            new ReportGenerateHandler(),
            new DataProcessHandler()
        ]);
    }
}

internal static class TestBackoff
{
    /// <summary>
    /// Defaults with no jitter, so a delay is exactly BaseDelay * Multiplier^(n-1).
    /// </summary>
    public static RetryBackoffPolicy Exact()
    {
        RetryPolicyOptions options = new() { JitterRatio = 0 };
        return new RetryBackoffPolicy(Options.Create(options), () => 0);
    }
}
