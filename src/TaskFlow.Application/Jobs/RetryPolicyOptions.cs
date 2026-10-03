namespace TaskFlow.Application.Jobs;

public sealed class RetryPolicyOptions
{
    public const string SectionName = "TaskFlow:Retry";

    /// <summary>
    /// Wait before the second attempt. Later waits grow by <see cref="Multiplier"/>.
    /// </summary>
    public TimeSpan BaseDelay { get; set; } = TimeSpan.FromSeconds(5);

    public double Multiplier { get; set; } = 5;

    /// <summary>
    /// No computed wait is longer than this, jitter included.
    /// </summary>
    public TimeSpan MaxDelay { get; set; } = TimeSpan.FromMinutes(10);

    /// <summary>
    /// Each wait is multiplied by a random factor in [1 - ratio, 1 + ratio], so retries do not line up.
    /// </summary>
    public double JitterRatio { get; set; } = 0.2;
}
