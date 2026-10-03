using TaskFlow.Domain.Jobs;
using TaskFlow.Infrastructure.Persistence;

using Microsoft.EntityFrameworkCore;

namespace TaskFlow.IntegrationTests;

[Collection(SqlServerCollection.Name)]
public sealed class JobPersistenceTests
{
    private readonly string _connectionString;

    public JobPersistenceTests(SqlServerFixture sql)
    {
        _connectionString = sql.ConnectionString;
    }

    [Fact]
    public async Task A_completed_attempt_round_trips_through_sql_server()
    {
        DateTimeOffset startedAt = new(2026, 10, 3, 15, 0, 0, TimeSpan.Zero);
        Guid id = Guid.NewGuid();
        Job job = Job.Create("demo.slow", "{\"seconds\":1}", 3, startedAt, "persist-1", id);
        job.MarkQueued(startedAt);
        job.StartProcessing("worker-a", startedAt);
        job.CompleteSuccessfully(startedAt.AddSeconds(2));

        await using (TaskFlowDbContext writing = CreateContext())
        {
            writing.Jobs.Add(job);
            await writing.SaveChangesAsync();
        }

        await using TaskFlowDbContext reading = CreateContext();
        Job? stored = await reading.Jobs
            .Include(candidate => candidate.Attempts)
            .SingleAsync(candidate => candidate.Id == id);

        Assert.Equal(JobStatus.Succeeded, stored.Status);
        Assert.Equal(1, stored.AttemptCount);
        Assert.Equal("persist-1", stored.CorrelationId);
        JobAttempt attempt = Assert.Single(stored.Attempts);
        Assert.Equal(1, attempt.AttemptNumber);
        Assert.Equal("worker-a", attempt.WorkerId);
        Assert.Equal(JobAttemptOutcome.Succeeded, attempt.Outcome);
        Assert.Equal(TimeSpan.FromSeconds(2), attempt.Duration);
        Assert.Equal(startedAt.AddSeconds(2), stored.LastCompletedAt);
    }

    private TaskFlowDbContext CreateContext()
    {
        DbContextOptions<TaskFlowDbContext> options = new DbContextOptionsBuilder<TaskFlowDbContext>()
            .UseSqlServer(_connectionString)
            .Options;
        return new TaskFlowDbContext(options);
    }
}
