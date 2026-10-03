namespace TaskFlow.Domain.Jobs;

/// <summary>
/// Input ceilings enforced by the domain. The retry schedule is not one of them.
/// </summary>
public static class JobLimits
{
    public const int MaxTypeLength = 128;
    public const int MaxPayloadLength = 65_536;
    public const int MaxCorrelationIdLength = 128;
    public const int MaxWorkerIdLength = 128;
    public const int MaxErrorTypeLength = 128;
    public const int MaxErrorMessageLength = 2_000;
}
