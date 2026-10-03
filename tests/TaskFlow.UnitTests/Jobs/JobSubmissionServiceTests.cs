using TaskFlow.Application.Jobs;
using TaskFlow.Domain.Jobs;

using Microsoft.Extensions.Options;

namespace TaskFlow.UnitTests.Jobs;

public sealed class JobSubmissionServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 3, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Submit_queues_the_job_after_a_confirmed_publish()
    {
        RecordingRepository repository = new();
        RecordingPublisher publisher = new();
        JobSubmissionService service = CreateService(repository, publisher);

        Job job = await service.SubmitAsync(
            new SubmitJob(" demo.success ", """{"message":"hello"}""", null, "corr-1"),
            CancellationToken.None);

        Assert.Equal(JobStatus.Queued, job.Status);
        Assert.Equal("demo.success", job.Type);
        Assert.Equal(5, job.MaxAttempts);
        Assert.Equal("corr-1", job.CorrelationId);
        Assert.Equal(Now, job.CreatedAt);
        Assert.Same(job, Assert.Single(repository.Added));
        Assert.Equal(1, repository.SaveCalls);
        Assert.Equal(new JobMessage(job.Id, "demo.success", "corr-1"), Assert.Single(publisher.Published));
    }

    [Fact]
    public async Task Submit_stores_the_row_before_publishing()
    {
        RecordingRepository repository = new();
        RecordingPublisher publisher = new() { OnPublish = message => Assert.NotNull(repository.Added.SingleOrDefault(job => job.Id == message.JobId)) };
        JobSubmissionService service = CreateService(repository, publisher);

        await service.SubmitAsync(new SubmitJob("demo.success", "{}", null, null), CancellationToken.None);

        Assert.Single(publisher.Published);
    }

    [Fact]
    public async Task Submit_leaves_the_job_pending_when_the_publish_fails()
    {
        RecordingRepository repository = new();
        RecordingPublisher publisher = new() { Failure = new JobPublishFailedException("RabbitMQ is unreachable.") };
        JobSubmissionService service = CreateService(repository, publisher);

        JobNotQueuedException exception = await Assert.ThrowsAsync<JobNotQueuedException>(
            () => service.SubmitAsync(new SubmitJob("demo.success", "{}", null, null), CancellationToken.None));

        Job stored = Assert.Single(repository.Added);
        Assert.Equal(stored.Id, exception.JobId);
        Assert.Equal(JobStatus.Pending, stored.Status);
        Assert.Equal(0, repository.SaveCalls);
        Assert.IsType<JobPublishFailedException>(exception.InnerException);
    }

    [Fact]
    public async Task Submit_reports_a_published_job_whose_queued_status_was_not_saved()
    {
        RecordingRepository repository = new() { SaveFailure = new JobDatabaseUnavailableException(new InvalidOperationException()) };
        RecordingPublisher publisher = new();
        JobSubmissionService service = CreateService(repository, publisher);

        JobQueuedStateNotSavedException exception = await Assert.ThrowsAsync<JobQueuedStateNotSavedException>(
            () => service.SubmitAsync(new SubmitJob("demo.success", "{}", null, null), CancellationToken.None));

        Assert.Equal(Assert.Single(repository.Added).Id, exception.JobId);
        Assert.Single(publisher.Published);
    }

    [Fact]
    public async Task Submit_generates_a_correlation_id_when_omitted()
    {
        RecordingPublisher publisher = new();
        JobSubmissionService service = CreateService(new RecordingRepository(), publisher);

        Job job = await service.SubmitAsync(
            new SubmitJob("demo.success", "{}", null, null),
            CancellationToken.None);

        Assert.False(string.IsNullOrWhiteSpace(job.CorrelationId));
        Assert.Equal(job.CorrelationId, Assert.Single(publisher.Published).CorrelationId);
    }

    [Fact]
    public async Task Submit_rejects_an_unknown_type_without_writing_or_publishing()
    {
        RecordingRepository repository = new();
        RecordingPublisher publisher = new();
        JobSubmissionService service = CreateService(repository, publisher);

        InvalidJobRequestException exception = await Assert.ThrowsAsync<InvalidJobRequestException>(
            () => service.SubmitAsync(new SubmitJob("not.a.job", "{}", null, null), CancellationToken.None));

        Assert.Contains("type", exception.Errors.Keys);
        Assert.Empty(repository.Added);
        Assert.Empty(publisher.Published);
    }

    [Theory]
    [InlineData("[]")]
    [InlineData("\"hello\"")]
    [InlineData("12")]
    [InlineData("not-json")]
    public async Task Submit_rejects_a_payload_that_is_not_a_json_object(string payload)
    {
        RecordingRepository repository = new();
        RecordingPublisher publisher = new();
        JobSubmissionService service = CreateService(repository, publisher);

        InvalidJobRequestException exception = await Assert.ThrowsAsync<InvalidJobRequestException>(
            () => service.SubmitAsync(new SubmitJob("demo.success", payload, null, null), CancellationToken.None));

        Assert.Contains("payload", exception.Errors.Keys);
        Assert.Empty(repository.Added);
        Assert.Empty(publisher.Published);
    }

    [Fact]
    public async Task Submit_rejects_an_attempt_budget_above_the_cap()
    {
        RecordingRepository repository = new();
        JobSubmissionService service = CreateService(repository, new RecordingPublisher());

        InvalidJobRequestException exception = await Assert.ThrowsAsync<InvalidJobRequestException>(
            () => service.SubmitAsync(new SubmitJob("demo.success", "{}", 21, null), CancellationToken.None));

        Assert.Contains("maxAttempts", exception.Errors.Keys);
        Assert.Empty(repository.Added);
    }

    [Fact]
    public void List_rejects_an_inverted_date_range_before_querying()
    {
        RecordingRepository repository = new();
        JobQueryService service = new(repository);
        JobListQuery query = new(
            null,
            null,
            Now.AddHours(1),
            Now,
            1,
            20);

        InvalidJobRequestException exception = Assert.Throws<InvalidJobRequestException>(
            () => service.ListAsync(query, CancellationToken.None).GetAwaiter().GetResult());

        Assert.Contains("createdFrom", exception.Errors.Keys);
        Assert.Equal(0, repository.ListCalls);
    }

    [Fact]
    public async Task Submit_rejects_a_type_whose_handler_waits_for_the_retry_policy()
    {
        RecordingRepository repository = new();
        RecordingPublisher publisher = new();
        JobSubmissionService service = CreateService(repository, publisher);

        InvalidJobRequestException exception = await Assert.ThrowsAsync<InvalidJobRequestException>(
            () => service.SubmitAsync(new SubmitJob("demo.transient-failure", "{}", null, null), CancellationToken.None));

        Assert.Contains("type", exception.Errors.Keys);
        Assert.Empty(repository.Added);
        Assert.Empty(publisher.Published);
    }

    private static JobSubmissionService CreateService(RecordingRepository repository, RecordingPublisher publisher)
    {
        IOptions<JobSubmissionOptions> options = Options.Create(new JobSubmissionOptions());
        FixedTimeProvider clock = new(Now);
        return new JobSubmissionService(repository, publisher, JobHandlerSet.Registry(clock), clock, options);
    }

    private sealed class RecordingPublisher : IJobPublisher
    {
        public List<JobMessage> Published { get; } = [];

        public JobPublishFailedException? Failure { get; init; }

        public Action<JobMessage>? OnPublish { get; init; }

        public Task PublishAsync(JobMessage message, CancellationToken cancellationToken)
        {
            if (Failure is not null)
            {
                throw Failure;
            }

            OnPublish?.Invoke(message);
            Published.Add(message);
            return Task.CompletedTask;
        }
    }

    private sealed class RecordingRepository : IJobRepository
    {
        public List<Job> Added { get; } = [];

        public int ListCalls { get; private set; }

        public int SaveCalls { get; private set; }

        public JobDatabaseUnavailableException? SaveFailure { get; init; }

        public Task AddAsync(Job job, CancellationToken cancellationToken)
        {
            Added.Add(job);
            return Task.CompletedTask;
        }

        public Task<Job?> GetAsync(Guid id, CancellationToken cancellationToken)
        {
            return Task.FromResult(Added.SingleOrDefault(job => job.Id == id));
        }

        public Task<JobPage> ListAsync(JobListQuery query, CancellationToken cancellationToken)
        {
            ListCalls++;
            return Task.FromResult(new JobPage([], query.Page, query.PageSize, 0));
        }

        public Task SaveChangesAsync(CancellationToken cancellationToken)
        {
            if (SaveFailure is not null)
            {
                throw SaveFailure;
            }

            SaveCalls++;
            return Task.CompletedTask;
        }
    }
}
