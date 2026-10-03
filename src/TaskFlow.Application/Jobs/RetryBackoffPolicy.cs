using Microsoft.Extensions.Options;

namespace TaskFlow.Application.Jobs;

/// <summary>
/// Wait before retry n is min(MaxDelay, BaseDelay * Multiplier^(n-1)), then spread by the jitter ratio.
/// </summary>
public sealed class RetryBackoffPolicy
{
    private readonly RetryPolicyOptions _options;
    private readonly Func<double> _randomUnit;

    public RetryBackoffPolicy(IOptions<RetryPolicyOptions> options, Func<double> randomUnit)
    {
        _options = options.Value;
        _randomUnit = randomUnit;
    }

    public TimeSpan GetDelay(int failedAttemptNumber)
    {
        if (failedAttemptNumber < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(failedAttemptNumber), "Attempt numbers start at 1.");
        }

        double grown = _options.BaseDelay.TotalSeconds * Math.Pow(_options.Multiplier, failedAttemptNumber - 1);
        double capped = Math.Min(_options.MaxDelay.TotalSeconds, grown);

        double unit = _randomUnit();
        if (unit is < 0 or > 1)
        {
            throw new InvalidOperationException("The jitter source must return a number from 0 to 1.");
        }

        double factor = 1 - _options.JitterRatio + (unit * 2 * _options.JitterRatio);
        double seconds = Math.Min(_options.MaxDelay.TotalSeconds, capped * factor);
        return TimeSpan.FromSeconds(seconds);
    }
}
