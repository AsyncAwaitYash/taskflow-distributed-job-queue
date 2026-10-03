using System.Text;
using System.Text.Json;

using TaskFlow.Domain.Jobs;
using TaskFlow.Infrastructure.Messaging;
using TaskFlow.Infrastructure.Persistence;

using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Hosting;

using RabbitMQ.Client;

namespace TaskFlow.IntegrationTests;

[Collection(InfrastructureCollection.Name)]
public sealed class WorkerTests : IAsyncLifetime
{
    private readonly SqlServerFixture _sql;
    private readonly RabbitMqFixture _rabbit;
    private readonly WebApplicationFactory<Program> _api;
    private readonly HttpClient _client;
    private IHost? _worker;

    public WorkerTests(SqlServerFixture sql, RabbitMqFixture rabbit)
    {
        _sql = sql;
        _rabbit = rabbit;
        _api = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseSetting("ConnectionStrings:TaskFlow", sql.ConnectionString);
            builder.UseSetting("ConnectionStrings:RabbitMq", rabbit.ConnectionString);
        });
        _client = _api.CreateClient();
    }

    public async Task InitializeAsync()
    {
        _worker = await TestWorkerHost.StartAsync(_sql, _rabbit, "test-worker");
        await TestWorkerHost.WaitForConsumersAsync(_rabbit, 1);
    }

    public async Task DisposeAsync()
    {
        await StopWorkerAsync();
        _client.Dispose();
        await _api.DisposeAsync();
    }

    [Fact]
    public async Task A_submitted_job_is_run_once_saved_as_succeeded_and_acked()
    {
        Guid jobId = await TestJobApi.SubmitAsync(_client, "demo.success", new { message = "hello" });

        JsonElement job = await TestJobApi.WaitForStatusAsync(_client, jobId, "Succeeded");

        JsonElement attempt = Assert.Single(job.GetProperty("attempts").EnumerateArray());
        Assert.Equal(1, attempt.GetProperty("attemptNumber").GetInt32());
        Assert.Equal("test-worker", attempt.GetProperty("workerId").GetString());
        Assert.Equal("Succeeded", attempt.GetProperty("outcome").GetString());

        await StopWorkerAsync();
        Assert.Equal(0u, await TestWorkerHost.ReadyMessageCountAsync(_rabbit));
    }

    [Fact]
    public async Task A_permanent_failure_is_saved_as_failed()
    {
        Guid jobId = await TestJobApi.SubmitAsync(_client, "demo.permanent-failure", new { });

        JsonElement job = await TestJobApi.WaitForStatusAsync(_client, jobId, "Failed");

        JsonElement attempt = Assert.Single(job.GetProperty("attempts").EnumerateArray());
        Assert.Equal("PermanentFailure", attempt.GetProperty("outcome").GetString());
        Assert.Equal("DemoPermanentFailure", attempt.GetProperty("errorType").GetString());
    }

    [Fact]
    public async Task A_duplicate_message_for_a_succeeded_job_is_acked_without_a_second_attempt()
    {
        Guid jobId = await TestJobApi.SubmitAsync(_client, "demo.success", new { });
        await TestJobApi.WaitForStatusAsync(_client, jobId, "Succeeded");

        await PublishRawAsync(JsonSerializer.SerializeToUtf8Bytes(
            new { jobId, type = "demo.success", correlationId = "duplicate" }));
        Guid marker = await TestJobApi.SubmitAsync(_client, "demo.success", new { });
        await TestJobApi.WaitForStatusAsync(_client, marker, "Succeeded");
        await StopWorkerAsync();

        JsonElement job = await TestJobApi.GetJobAsync(_client, jobId);
        Assert.Single(job.GetProperty("attempts").EnumerateArray());
        Assert.Equal(0u, await TestWorkerHost.ReadyMessageCountAsync(_rabbit));
    }

    [Fact]
    public async Task A_message_for_a_pending_row_promotes_and_runs_the_job()
    {
        Job pending = Job.Create("report.generate", """{"name":"daily"}""", 3, DateTimeOffset.UtcNow, "pending-row");
        await using (TaskFlowDbContext db = _sql.CreateContext())
        {
            db.Jobs.Add(pending);
            await db.SaveChangesAsync();
        }

        await PublishRawAsync(JsonSerializer.SerializeToUtf8Bytes(
            new { jobId = pending.Id, type = pending.Type, correlationId = pending.CorrelationId }));

        JsonElement job = await TestJobApi.WaitForStatusAsync(_client, pending.Id, "Succeeded");
        Assert.Single(job.GetProperty("attempts").EnumerateArray());
    }

    [Fact]
    public async Task An_unreadable_message_is_rejected_and_does_not_block_the_queue()
    {
        await PublishRawAsync(Encoding.UTF8.GetBytes("not json"));

        Guid marker = await TestJobApi.SubmitAsync(_client, "data.process", new { rows = 3 });
        await TestJobApi.WaitForStatusAsync(_client, marker, "Succeeded");
        await StopWorkerAsync();

        Assert.Equal(0u, await TestWorkerHost.ReadyMessageCountAsync(_rabbit));
    }

    private async Task PublishRawAsync(byte[] body)
    {
        await using IConnection connection = await _rabbit.ConnectAsync();
        await using IChannel channel = await connection.CreateChannelAsync(
            new CreateChannelOptions(publisherConfirmationsEnabled: true, publisherConfirmationTrackingEnabled: true));
        await RabbitMqTopology.DeclareAsync(channel, CancellationToken.None);
        await channel.BasicPublishAsync(
            RabbitMqTopology.Exchange,
            RabbitMqTopology.ProcessRoutingKey,
            mandatory: true,
            new BasicProperties { Persistent = true },
            body);
    }

    private async Task StopWorkerAsync()
    {
        await TestWorkerHost.StopAsync(_worker);
        _worker = null;
    }
}
