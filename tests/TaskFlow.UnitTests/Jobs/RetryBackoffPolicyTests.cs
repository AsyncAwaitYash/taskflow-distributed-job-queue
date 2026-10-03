using TaskFlow.Application.Jobs;

using Microsoft.Extensions.Options;

namespace TaskFlow.UnitTests.Jobs;

public sealed class RetryBackoffPolicyTests
{
    [Theory]
    [InlineData(1, 5)]
    [InlineData(2, 25)]
    [InlineData(3, 125)]
    [InlineData(4, 600)]
    [InlineData(12, 600)]
    public void Delay_stays_within_the_jitter_band_and_never_past_the_cap(int attempt, int expectedSeconds)
    {
        TimeSpan low = Policy(() => 0).GetDelay(attempt);
        TimeSpan high = Policy(() => 1).GetDelay(attempt);

        Assert.Equal(TimeSpan.FromSeconds(expectedSeconds * 0.8), low);
        Assert.Equal(TimeSpan.FromSeconds(Math.Min(600, expectedSeconds * 1.2)), high);
    }

    [Theory]
    [InlineData(1, 5)]
    [InlineData(2, 25)]
    [InlineData(3, 125)]
    [InlineData(4, 600)]
    public void Delay_is_exact_when_jitter_is_off(int attempt, int expectedSeconds)
    {
        RetryBackoffPolicy policy = new(Options.Create(new RetryPolicyOptions { JitterRatio = 0 }), () => 0.5);

        Assert.Equal(TimeSpan.FromSeconds(expectedSeconds), policy.GetDelay(attempt));
    }

    [Fact]
    public void Delay_rejects_an_attempt_number_below_one()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Policy(() => 0).GetDelay(0));
    }

    private static RetryBackoffPolicy Policy(Func<double> randomUnit)
    {
        return new RetryBackoffPolicy(Options.Create(new RetryPolicyOptions()), randomUnit);
    }
}
