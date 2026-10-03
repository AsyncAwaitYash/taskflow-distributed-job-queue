using TaskFlow.Application.Jobs.Handlers;

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
            new DemoSlowHandler(clock),
            new EmailSendHandler(),
            new ReportGenerateHandler(),
            new DataProcessHandler()
        ]);
    }
}
