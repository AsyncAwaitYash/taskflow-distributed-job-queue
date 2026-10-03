using TaskFlow.Domain.Jobs;

namespace TaskFlow.UnitTests.Jobs;

public sealed class JobTransitionMapTests
{
    public static TheoryData<JobStatus, JobStatus> LegalTransitions { get; } = new()
    {
        { JobStatus.Pending, JobStatus.Queued },
        { JobStatus.Queued, JobStatus.Processing },
        { JobStatus.Processing, JobStatus.Succeeded },
        { JobStatus.Processing, JobStatus.RetryScheduled },
        { JobStatus.Processing, JobStatus.Failed },
        { JobStatus.Processing, JobStatus.DeadLettered },
        { JobStatus.RetryScheduled, JobStatus.Queued },
        { JobStatus.RetryScheduled, JobStatus.DeadLettered },
        { JobStatus.Failed, JobStatus.Queued },
        { JobStatus.DeadLettered, JobStatus.Queued }
    };

    [Theory]
    [MemberData(nameof(LegalTransitions))]
    public void Legal_pairs_are_allowed(JobStatus from, JobStatus to)
    {
        Assert.True(JobTransitions.CanTransition(from, to));
    }

    [Fact]
    public void Every_other_pair_is_rejected()
    {
        HashSet<(JobStatus From, JobStatus To)> legal = LegalTransitions
            .Select(row => ((JobStatus)row[0], (JobStatus)row[1]))
            .ToHashSet();

        foreach (JobStatus from in Enum.GetValues<JobStatus>())
        {
            foreach (JobStatus to in Enum.GetValues<JobStatus>())
            {
                bool allowed = JobTransitions.CanTransition(from, to);
                Assert.Equal(legal.Contains((from, to)), allowed);
            }
        }
    }

    [Fact]
    public void Ensure_names_the_illegal_pair()
    {
        InvalidJobTransitionException exception = Assert.Throws<InvalidJobTransitionException>(
            () => JobTransitions.Ensure(JobStatus.Succeeded, JobStatus.Processing));

        Assert.Equal(JobStatus.Succeeded, exception.From);
        Assert.Equal(JobStatus.Processing, exception.To);
    }
}
