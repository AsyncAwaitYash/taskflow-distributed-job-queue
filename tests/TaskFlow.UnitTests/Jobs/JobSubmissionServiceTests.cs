using TaskFlow.Application.Jobs;
using TaskFlow.Domain.Jobs;

using Microsoft.Extensions.Options;

namespace TaskFlow.UnitTests.Jobs;

public sealed class JobSubmissionServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 3, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Submit_stores_a_pending_job_with_the_default_attempt_budget()
    {
        RecordingRepository repository = new();
        JobSubmissionService service = CreateService(repository);

        Job job = await service.SubmitAsync(
            new SubmitJob(" demo.success ", """{"message":"hello"}""", null, "corr-1"),
            CancellationToken.None);

        Assert.Equal(JobStatus.Pending, job.Status);
        Assert.Equal("demo.success", job.Type);
        Assert.Equal(5, job.MaxAttempts);
        Assert.Equal("corr-1", job.CorrelationId);
        Assert.Equal(Now, job.CreatedAt);
        Assert.Same(job, Assert.Single(repository.Added));
    }

    [Fact]
    public async Task Submit_generates_a_correlation_id_when_omitted()
    {
        JobSubmissionService service = CreateService(new RecordingRepository());

        Job job = await service.SubmitAsync(
            new SubmitJob("demo.success", "{}", null, null),
            CancellationToken.None);

        Assert.False(string.IsNullOrWhiteSpace(job.CorrelationId));
    }

    [Fact]
    public async Task Submit_rejects_an_unknown_type_without_writing()
    {
        RecordingRepository repository = new();
        JobSubmissionService service = CreateService(repository);

        InvalidJobRequestException exception = await Assert.ThrowsAsync<InvalidJobRequestException>(
            () => service.SubmitAsync(new SubmitJob("not.a.job", "{}", null, null), CancellationToken.None));

        Assert.Contains("type", exception.Errors.Keys);
        Assert.Empty(repository.Added);
    }

    [Theory]
    [InlineData("[]")]
    [InlineData("\"hello\"")]
    [InlineData("12")]
    [InlineData("not-json")]
    public async Task Submit_rejects_a_payload_that_is_not_a_json_object(string payload)
    {
        RecordingRepository repository = new();
        JobSubmissionService service = CreateService(repository);

        InvalidJobRequestException exception = await Assert.ThrowsAsync<InvalidJobRequestException>(
            () => service.SubmitAsync(new SubmitJob("demo.success", payload, null, null), CancellationToken.None));

        Assert.Contains("payload", exception.Errors.Keys);
        Assert.Empty(repository.Added);
    }

    [Fact]
    public async Task Submit_rejects_an_attempt_budget_above_the_cap()
    {
        RecordingRepository repository = new();
        JobSubmissionService service = CreateService(repository);

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

    private static JobSubmissionService CreateService(RecordingRepository repository)
    {
        IOptions<JobSubmissionOptions> options = Options.Create(new JobSubmissionOptions());
        return new JobSubmissionService(repository, new FixedTimeProvider(Now), options);
    }

    private sealed class FixedTimeProvider : TimeProvider
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

    private sealed class RecordingRepository : IJobRepository
    {
        public List<Job> Added { get; } = [];

        public int ListCalls { get; private set; }

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
    }
}
