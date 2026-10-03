using TaskFlow.Application.Jobs;
using TaskFlow.Application.Jobs.Handlers;
using TaskFlow.Domain.Jobs;

namespace TaskFlow.UnitTests.Jobs;

public sealed class JobProcessorTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 3, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task A_queued_job_is_claimed_run_and_saved_as_succeeded()
    {
        Job job = QueuedJob("demo.success");
        TrackingRepository repository = new(job);
        CountingHandler handler = new("demo.success", JobHandlerResult.Success());

        JobProcessingOutcome outcome = await CreateProcessor(repository, handler).ProcessAsync(job.Id, "worker-1", CancellationToken.None);

        Assert.Equal(JobProcessingOutcome.Succeeded, outcome);
        Assert.Equal(JobStatus.Succeeded, job.Status);
        JobAttempt attempt = Assert.Single(job.Attempts);
        Assert.Equal(JobAttemptOutcome.Succeeded, attempt.Outcome);
        Assert.Equal("worker-1", attempt.WorkerId);
        Assert.Equal(1, handler.Calls);
        Assert.Equal([JobStatus.Processing, JobStatus.Succeeded], repository.SavedStatuses);
    }

    [Fact]
    public async Task A_permanent_failure_result_moves_the_job_to_failed()
    {
        Job job = QueuedJob("demo.permanent-failure");
        TrackingRepository repository = new(job);
        JobProcessor processor = new(repository, JobHandlerSet.Registry(new FixedTimeProvider(Now)), new FixedTimeProvider(Now));

        JobProcessingOutcome outcome = await processor.ProcessAsync(job.Id, "worker-1", CancellationToken.None);

        Assert.Equal(JobProcessingOutcome.Failed, outcome);
        Assert.Equal(JobStatus.Failed, job.Status);
        Assert.Equal("DemoPermanentFailure", Assert.Single(job.Attempts).ErrorType);
    }

    [Fact]
    public async Task A_pending_row_with_a_delivered_message_is_promoted_and_processed()
    {
        Job job = Job.Create("demo.success", "{}", 3, Now);
        TrackingRepository repository = new(job);
        CountingHandler handler = new("demo.success", JobHandlerResult.Success());

        JobProcessingOutcome outcome = await CreateProcessor(repository, handler).ProcessAsync(job.Id, "worker-1", CancellationToken.None);

        Assert.Equal(JobProcessingOutcome.Succeeded, outcome);
        Assert.Equal(JobStatus.Succeeded, job.Status);
        Assert.Equal(1, handler.Calls);
    }

    [Fact]
    public async Task A_redelivered_succeeded_job_is_skipped_without_running_the_handler()
    {
        Job job = QueuedJob("demo.success");
        job.StartProcessing("worker-1", Now);
        job.CompleteSuccessfully(Now);
        TrackingRepository repository = new(job);
        CountingHandler handler = new("demo.success", JobHandlerResult.Success());

        JobProcessingOutcome outcome = await CreateProcessor(repository, handler).ProcessAsync(job.Id, "worker-2", CancellationToken.None);

        Assert.Equal(JobProcessingOutcome.SkippedNotQueued, outcome);
        Assert.Equal(0, handler.Calls);
        Assert.Single(job.Attempts);
        Assert.Empty(repository.SavedStatuses);
    }

    [Fact]
    public async Task A_job_already_processing_is_skipped_without_a_second_attempt()
    {
        Job job = QueuedJob("demo.success");
        job.StartProcessing("worker-1", Now);
        TrackingRepository repository = new(job);
        CountingHandler handler = new("demo.success", JobHandlerResult.Success());

        JobProcessingOutcome outcome = await CreateProcessor(repository, handler).ProcessAsync(job.Id, "worker-2", CancellationToken.None);

        Assert.Equal(JobProcessingOutcome.SkippedNotQueued, outcome);
        Assert.Equal(0, handler.Calls);
        Assert.Single(job.Attempts);
    }

    [Fact]
    public async Task An_unknown_job_id_is_skipped()
    {
        TrackingRepository repository = new();
        JobProcessor processor = CreateProcessor(repository, new CountingHandler("demo.success", JobHandlerResult.Success()));

        JobProcessingOutcome outcome = await processor.ProcessAsync(Guid.NewGuid(), "worker-1", CancellationToken.None);

        Assert.Equal(JobProcessingOutcome.SkippedNotFound, outcome);
        Assert.Empty(repository.SavedStatuses);
    }

    [Fact]
    public async Task A_type_without_a_handler_fails_permanently()
    {
        Job job = QueuedJob("demo.transient-failure");
        TrackingRepository repository = new(job);

        JobProcessingOutcome outcome = await CreateProcessor(repository).ProcessAsync(job.Id, "worker-1", CancellationToken.None);

        Assert.Equal(JobProcessingOutcome.Failed, outcome);
        Assert.Equal(JobProcessor.NoHandlerRegistered, Assert.Single(job.Attempts).ErrorType);
    }

    [Fact]
    public async Task A_handler_exception_fails_the_job_without_storing_the_exception_message()
    {
        Job job = QueuedJob("demo.success");
        TrackingRepository repository = new(job);
        ThrowingHandler handler = new("demo.success", new InvalidOperationException("secret payload value"));

        JobProcessingOutcome outcome = await CreateProcessor(repository, handler).ProcessAsync(job.Id, "worker-1", CancellationToken.None);

        Assert.Equal(JobProcessingOutcome.Failed, outcome);
        JobAttempt attempt = Assert.Single(job.Attempts);
        Assert.Equal(nameof(InvalidOperationException), attempt.ErrorType);
        Assert.DoesNotContain("secret", attempt.ErrorMessage, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_database_failure_on_the_claim_propagates_before_the_handler_runs()
    {
        Job job = QueuedJob("demo.success");
        TrackingRepository repository = new(job) { SaveFailure = new JobDatabaseUnavailableException(new InvalidOperationException()) };
        CountingHandler handler = new("demo.success", JobHandlerResult.Success());

        await Assert.ThrowsAsync<JobDatabaseUnavailableException>(
            () => CreateProcessor(repository, handler).ProcessAsync(job.Id, "worker-1", CancellationToken.None));

        Assert.Equal(0, handler.Calls);
    }

    [Fact]
    public void The_registry_rejects_two_handlers_for_one_type()
    {
        Assert.Throws<InvalidOperationException>(() => new JobHandlerRegistry(
        [
            new CountingHandler("demo.success", JobHandlerResult.Success()),
            new CountingHandler("demo.success", JobHandlerResult.Success())
        ]));
    }

    [Fact]
    public void The_registry_does_not_include_the_transient_failure_demo_yet()
    {
        JobHandlerRegistry registry = JobHandlerSet.Registry(new FixedTimeProvider(Now));

        Assert.False(registry.IsRegistered("demo.transient-failure"));
        Assert.True(registry.IsRegistered("demo.slow"));
        Assert.Equal(6, registry.Types.Count);
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("""{"seconds":"5"}""")]
    [InlineData("""{"seconds":-1}""")]
    [InlineData("""{"seconds":31}""")]
    [InlineData("""{"seconds":1.5}""")]
    public async Task Demo_slow_rejects_a_bad_duration_as_a_permanent_failure(string payload)
    {
        DemoSlowHandler handler = new(new FixedTimeProvider(Now));

        JobHandlerResult result = await handler.HandleAsync(
            new JobContext(Guid.NewGuid(), "demo.slow", payload, 1, "corr"),
            CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal("InvalidPayload", result.ErrorType);
    }

    [Fact]
    public async Task Demo_slow_succeeds_for_a_zero_second_wait()
    {
        DemoSlowHandler handler = new(TimeProvider.System);

        JobHandlerResult result = await handler.HandleAsync(
            new JobContext(Guid.NewGuid(), "demo.slow", """{"seconds":0}""", 1, "corr"),
            CancellationToken.None);

        Assert.True(result.Succeeded);
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("""{"to":""}""")]
    [InlineData("""{"to":42}""")]
    public async Task Email_send_needs_a_recipient(string payload)
    {
        JobHandlerResult result = await new EmailSendHandler().HandleAsync(
            new JobContext(Guid.NewGuid(), "email.send", payload, 1, "corr"),
            CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal("InvalidPayload", result.ErrorType);
    }

    private static Job QueuedJob(string type)
    {
        Job job = Job.Create(type, "{}", 3, Now);
        job.MarkQueued(Now);
        return job;
    }

    private static JobProcessor CreateProcessor(TrackingRepository repository, params IJobHandler[] handlers)
    {
        FixedTimeProvider clock = new(Now);
        return new JobProcessor(repository, new JobHandlerRegistry(handlers), clock);
    }

    private sealed class CountingHandler : IJobHandler
    {
        private readonly JobHandlerResult _result;

        public CountingHandler(string type, JobHandlerResult result)
        {
            Type = type;
            _result = result;
        }

        public string Type { get; }

        public int Calls { get; private set; }

        public Task<JobHandlerResult> HandleAsync(JobContext context, CancellationToken cancellationToken)
        {
            Calls++;
            return Task.FromResult(_result);
        }
    }

    private sealed class ThrowingHandler : IJobHandler
    {
        private readonly Exception _exception;

        public ThrowingHandler(string type, Exception exception)
        {
            Type = type;
            _exception = exception;
        }

        public string Type { get; }

        public Task<JobHandlerResult> HandleAsync(JobContext context, CancellationToken cancellationToken)
        {
            throw _exception;
        }
    }

    private sealed class TrackingRepository : IJobRepository
    {
        private readonly Dictionary<Guid, Job> _jobs;

        public TrackingRepository(params Job[] jobs)
        {
            _jobs = jobs.ToDictionary(job => job.Id);
        }

        public List<JobStatus> SavedStatuses { get; } = [];

        public JobDatabaseUnavailableException? SaveFailure { get; init; }

        public Task AddAsync(Job job, CancellationToken cancellationToken)
        {
            _jobs.Add(job.Id, job);
            return Task.CompletedTask;
        }

        public Task<Job?> GetAsync(Guid id, CancellationToken cancellationToken)
        {
            return Task.FromResult(_jobs.GetValueOrDefault(id));
        }

        public Task<JobPage> ListAsync(JobListQuery query, CancellationToken cancellationToken)
        {
            return Task.FromResult(new JobPage([], query.Page, query.PageSize, 0));
        }

        public Task SaveChangesAsync(CancellationToken cancellationToken)
        {
            if (SaveFailure is not null)
            {
                throw SaveFailure;
            }

            SavedStatuses.AddRange(_jobs.Values.Select(job => job.Status));
            return Task.CompletedTask;
        }
    }
}
