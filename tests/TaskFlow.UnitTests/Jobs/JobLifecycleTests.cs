using TaskFlow.Domain.Jobs;

namespace TaskFlow.UnitTests.Jobs;

public sealed class JobLifecycleTests
{
    private static readonly DateTimeOffset CreatedAt = new(2026, 10, 3, 12, 0, 0, TimeSpan.Zero);
    private static readonly Guid JobId = Guid.Parse("11111111-1111-1111-1111-111111111111");

    [Fact]
    public void Create_starts_pending_and_keeps_the_supplied_identity()
    {
        Job job = Job.Create(" demo.success ", "{\"message\":\"hello\"}", 5, CreatedAt, " corr-1 ", JobId);

        Assert.Equal(JobId, job.Id);
        Assert.Equal("demo.success", job.Type);
        Assert.Equal("{\"message\":\"hello\"}", job.Payload);
        Assert.Equal(JobStatus.Pending, job.Status);
        Assert.Equal(0, job.AttemptCount);
        Assert.Equal(5, job.MaxAttempts);
        Assert.Equal("corr-1", job.CorrelationId);
        Assert.Empty(job.Attempts);
        Assert.Null(job.NextAttemptAt);
        Assert.Equal(CreatedAt, job.CreatedAt);
        Assert.Equal(CreatedAt, job.UpdatedAt);
    }

    [Fact]
    public void Create_generates_a_correlation_id_when_one_is_omitted()
    {
        Job job = Job.Create("demo.success", "{}", 1, CreatedAt);

        Assert.False(string.IsNullOrWhiteSpace(job.CorrelationId));
        Assert.NotEqual(Guid.Empty, job.Id);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Create_rejects_a_non_positive_attempt_budget(int maxAttempts)
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => Job.Create("demo.success", "{}", maxAttempts, CreatedAt));
    }

    [Fact]
    public void Create_rejects_a_blank_type_or_payload()
    {
        Assert.Throws<ArgumentException>(() => Job.Create("  ", "{}", 1, CreatedAt));
        Assert.Throws<ArgumentException>(() => Job.Create("demo.success", "   ", 1, CreatedAt));
        Assert.Throws<ArgumentNullException>(() => Job.Create("demo.success", null!, 1, CreatedAt));
    }

    [Fact]
    public void Happy_path_records_one_successful_attempt()
    {
        Job job = NewJob();
        DateTimeOffset queuedAt = CreatedAt.AddSeconds(1);
        DateTimeOffset startedAt = CreatedAt.AddSeconds(2);
        DateTimeOffset completedAt = CreatedAt.AddSeconds(5);

        job.MarkQueued(queuedAt);
        JobAttempt attempt = job.StartProcessing("worker-1", startedAt);
        job.CompleteSuccessfully(completedAt);

        Assert.Equal(JobStatus.Succeeded, job.Status);
        Assert.Equal(1, job.AttemptCount);
        Assert.Null(job.LastError);
        Assert.Null(job.NextAttemptAt);
        Assert.Equal(startedAt, job.LastStartedAt);
        Assert.Equal(completedAt, job.LastCompletedAt);
        Assert.Equal(JobId, attempt.JobId);
        Assert.Equal(1, attempt.AttemptNumber);
        Assert.Equal("worker-1", attempt.WorkerId);
        Assert.Equal(JobAttemptOutcome.Succeeded, attempt.Outcome);
        Assert.Equal(TimeSpan.FromSeconds(3), attempt.Duration);
        Assert.False(attempt.IsOpen);
        JobAttempt recorded = Assert.Single(job.Attempts);
        Assert.Same(attempt, recorded);
    }

    [Fact]
    public void Retryable_failure_schedules_the_next_try_then_succeeds()
    {
        Job job = NewJob(maxAttempts: 3);
        job.MarkQueued(CreatedAt);
        job.StartProcessing("worker-1", CreatedAt);
        DateTimeOffset failedAt = CreatedAt.AddSeconds(1);
        DateTimeOffset nextAttemptAt = failedAt.AddSeconds(5);

        job.RecordRetryableFailure("timeout", "dependency timed out", failedAt, nextAttemptAt);

        Assert.Equal(JobStatus.RetryScheduled, job.Status);
        Assert.Equal(nextAttemptAt, job.NextAttemptAt);
        Assert.Equal("dependency timed out", job.LastError);
        Assert.Equal(JobAttemptOutcome.RetryableFailure, job.Attempts[0].Outcome);
        Assert.Equal("timeout", job.Attempts[0].ErrorType);

        job.MarkQueued(nextAttemptAt);
        Assert.Null(job.NextAttemptAt);
        Assert.Equal(JobStatus.Queued, job.Status);

        job.StartProcessing("worker-2", nextAttemptAt);
        job.CompleteSuccessfully(nextAttemptAt.AddSeconds(1));

        Assert.Equal(JobStatus.Succeeded, job.Status);
        Assert.Equal(2, job.AttemptCount);
        Assert.Equal(2, job.Attempts.Count);
        Assert.Equal([1, 2], job.Attempts.Select(attempt => attempt.AttemptNumber).ToArray());
        Assert.Null(job.LastError);
    }

    [Fact]
    public void Permanent_failure_does_not_schedule_a_retry()
    {
        Job job = NewJob(maxAttempts: 5);
        job.MarkQueued(CreatedAt);
        job.StartProcessing("worker-1", CreatedAt);

        job.FailPermanently("permanent", "bad request", CreatedAt.AddSeconds(1));

        Assert.Equal(JobStatus.Failed, job.Status);
        Assert.Equal(1, job.AttemptCount);
        Assert.Null(job.NextAttemptAt);
        Assert.Equal("bad request", job.LastError);
        Assert.Equal(JobAttemptOutcome.PermanentFailure, Assert.Single(job.Attempts).Outcome);
    }

    [Fact]
    public void Exhausting_the_budget_dead_letters_instead_of_scheduling()
    {
        Job job = NewJob(maxAttempts: 2);
        job.MarkQueued(CreatedAt);
        job.StartProcessing("worker-1", CreatedAt);
        job.RecordRetryableFailure("timeout", "first", CreatedAt.AddSeconds(1), CreatedAt.AddSeconds(5));
        job.MarkQueued(CreatedAt.AddSeconds(5));
        job.StartProcessing("worker-2", CreatedAt.AddSeconds(5));

        job.RecordRetryableFailure("timeout", "second", CreatedAt.AddSeconds(6), CreatedAt.AddMinutes(10));

        Assert.Equal(JobStatus.DeadLettered, job.Status);
        Assert.Equal(2, job.AttemptCount);
        Assert.Null(job.NextAttemptAt);
        Assert.Equal("second", job.LastError);
        Assert.All(job.Attempts, attempt => Assert.Equal(JobAttemptOutcome.RetryableFailure, attempt.Outcome));
    }

    [Fact]
    public void A_single_allowed_attempt_dead_letters_on_the_first_retryable_failure()
    {
        Job job = NewJob(maxAttempts: 1);
        job.MarkQueued(CreatedAt);
        job.StartProcessing("worker-1", CreatedAt);

        job.RecordRetryableFailure("timeout", "only try", CreatedAt, null);

        Assert.Equal(JobStatus.DeadLettered, job.Status);
        Assert.Null(job.NextAttemptAt);
    }

    [Fact]
    public void Waiting_retry_can_be_dead_lettered_without_a_new_attempt()
    {
        Job job = ScheduledRetry();
        int attemptsBefore = job.Attempts.Count;

        job.DeadLetter("gave up before republish", CreatedAt.AddMinutes(1));

        Assert.Equal(JobStatus.DeadLettered, job.Status);
        Assert.Equal(attemptsBefore, job.Attempts.Count);
        Assert.Null(job.NextAttemptAt);
        Assert.Equal("gave up before republish", job.LastError);
    }

    [Fact]
    public void Manual_retry_requeues_without_resetting_the_attempt_count()
    {
        Job job = NewJob(maxAttempts: 5);
        job.MarkQueued(CreatedAt);
        job.StartProcessing("worker-1", CreatedAt);
        job.FailPermanently("permanent", "nope", CreatedAt.AddSeconds(1));

        job.RetryManually(CreatedAt.AddSeconds(2));
        job.StartProcessing("worker-2", CreatedAt.AddSeconds(2));

        Assert.Equal(JobStatus.Processing, job.Status);
        Assert.Equal(2, job.AttemptCount);
        Assert.Null(job.NextAttemptAt);
        Assert.Equal("nope", job.LastError);
    }

    [Fact]
    public void Manual_retry_also_accepts_a_dead_lettered_job()
    {
        Job job = NewJob(maxAttempts: 1);
        job.MarkQueued(CreatedAt);
        job.StartProcessing("worker-1", CreatedAt);
        job.RecordRetryableFailure("timeout", "spent", CreatedAt, null);

        job.RetryManually(CreatedAt.AddSeconds(1));

        Assert.Equal(JobStatus.Queued, job.Status);
        Assert.Equal(1, job.AttemptCount);
    }

    [Fact]
    public void A_terminal_job_cannot_start_another_attempt()
    {
        Job job = NewJob();
        job.MarkQueued(CreatedAt);
        job.StartProcessing("worker-1", CreatedAt);
        job.CompleteSuccessfully(CreatedAt);

        InvalidJobTransitionException exception = Assert.Throws<InvalidJobTransitionException>(
            () => job.StartProcessing("worker-2", CreatedAt));

        Assert.Equal(JobStatus.Succeeded, exception.From);
        Assert.Equal(JobStatus.Processing, exception.To);
        Assert.Equal(1, job.AttemptCount);
    }

    [Fact]
    public void Illegal_transitions_leave_the_job_unchanged()
    {
        Job job = NewJob();

        Assert.Throws<InvalidJobTransitionException>(() => job.StartProcessing("worker-1", CreatedAt));
        Assert.Equal(JobStatus.Pending, job.Status);
        Assert.Equal(0, job.AttemptCount);

        job.MarkQueued(CreatedAt);
        Assert.Throws<InvalidJobTransitionException>(() => job.CompleteSuccessfully(CreatedAt));
        Assert.Throws<InvalidJobTransitionException>(() => job.FailPermanently("permanent", "nope", CreatedAt));
        Assert.Equal(JobStatus.Queued, job.Status);
        Assert.Empty(job.Attempts);
    }

    [Fact]
    public void A_retry_without_a_future_time_does_not_close_the_attempt()
    {
        Job job = NewJob();
        job.MarkQueued(CreatedAt);
        JobAttempt attempt = job.StartProcessing("worker-1", CreatedAt);

        Assert.Throws<ArgumentException>(
            () => job.RecordRetryableFailure("timeout", "later", CreatedAt.AddSeconds(2), CreatedAt.AddSeconds(1)));
        Assert.Throws<ArgumentException>(
            () => job.RecordRetryableFailure("timeout", "missing", CreatedAt.AddSeconds(2), null));

        Assert.Equal(JobStatus.Processing, job.Status);
        Assert.True(attempt.IsOpen);
        Assert.Null(job.NextAttemptAt);
    }

    [Fact]
    public void A_blank_worker_id_does_not_increment_the_attempt_count()
    {
        Job job = NewJob();
        job.MarkQueued(CreatedAt);

        Assert.Throws<ArgumentException>(() => job.StartProcessing(" ", CreatedAt));

        Assert.Equal(JobStatus.Queued, job.Status);
        Assert.Equal(0, job.AttemptCount);
    }

    [Fact]
    public void An_earlier_clock_is_rejected()
    {
        Job job = NewJob();
        job.MarkQueued(CreatedAt.AddMinutes(1));

        Assert.Throws<ArgumentOutOfRangeException>(() => job.StartProcessing("worker-1", CreatedAt));
        Assert.Equal(JobStatus.Queued, job.Status);
    }

    [Fact]
    public void Succeeded_jobs_cannot_be_manually_retried()
    {
        Job job = NewJob();
        job.MarkQueued(CreatedAt);
        job.StartProcessing("worker-1", CreatedAt);
        job.CompleteSuccessfully(CreatedAt);

        Assert.Throws<InvalidJobTransitionException>(() => job.RetryManually(CreatedAt));
        Assert.Equal(JobStatus.Succeeded, job.Status);
    }

    [Fact]
    public void Publishing_and_manual_retry_are_different_entry_points()
    {
        Job pending = NewJob();
        Assert.Throws<InvalidOperationException>(() => pending.RetryManually(CreatedAt));

        Job failed = NewJob();
        failed.MarkQueued(CreatedAt);
        failed.StartProcessing("worker-1", CreatedAt);
        failed.FailPermanently("permanent", "nope", CreatedAt);
        Assert.Throws<InvalidOperationException>(() => failed.MarkQueued(CreatedAt));
        Assert.Throws<InvalidJobTransitionException>(() => failed.DeadLetter("no", CreatedAt));
    }

    private static Job NewJob(int maxAttempts = 5)
    {
        return Job.Create("demo.success", "{\"message\":\"hello\"}", maxAttempts, CreatedAt, "corr-1", JobId);
    }

    private static Job ScheduledRetry()
    {
        Job job = NewJob();
        job.MarkQueued(CreatedAt);
        job.StartProcessing("worker-1", CreatedAt);
        job.RecordRetryableFailure("timeout", "first", CreatedAt, CreatedAt.AddSeconds(5));
        return job;
    }
}
