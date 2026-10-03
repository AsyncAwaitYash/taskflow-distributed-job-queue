using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;

using TaskFlow.Application;
using TaskFlow.Domain.Jobs;
using TaskFlow.Infrastructure;
using TaskFlow.Infrastructure.Messaging;
using TaskFlow.Infrastructure.Persistence;

using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

using RabbitMQ.Client;

namespace TaskFlow.IntegrationTests;

[Collection(InfrastructureCollection.Name)]
public sealed class WorkerTests : IAsyncLifetime
{
    private static readonly TimeSpan WaitLimit = TimeSpan.FromSeconds(30);

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
        HostApplicationBuilder builder = Host.CreateApplicationBuilder();
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:TaskFlow"] = _sql.ConnectionString,
            ["ConnectionStrings:RabbitMq"] = _rabbit.ConnectionString,
            ["TaskFlow:Worker:WorkerId"] = "test-worker"
        });
        builder.Services.AddTaskFlowApplication(builder.Configuration);
        TaskFlowInfrastructureStatus infrastructure = builder.Services.AddTaskFlowInfrastructure(builder.Configuration, "taskflow-worker-test");
        builder.Services.AddTaskFlowJobConsumer(builder.Configuration, infrastructure);

        _worker = builder.Build();
        await _worker.StartAsync();
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
        Guid jobId = await SubmitAsync("demo.success", new { message = "hello" });

        JsonElement job = await WaitForStatusAsync(jobId, "Succeeded");

        JsonElement attempt = Assert.Single(job.GetProperty("attempts").EnumerateArray());
        Assert.Equal(1, attempt.GetProperty("attemptNumber").GetInt32());
        Assert.Equal("test-worker", attempt.GetProperty("workerId").GetString());
        Assert.Equal("Succeeded", attempt.GetProperty("outcome").GetString());

        await StopWorkerAsync();
        Assert.Equal(0u, await ReadyMessageCountAsync());
    }

    [Fact]
    public async Task A_permanent_failure_is_saved_as_failed()
    {
        Guid jobId = await SubmitAsync("demo.permanent-failure", new { });

        JsonElement job = await WaitForStatusAsync(jobId, "Failed");

        JsonElement attempt = Assert.Single(job.GetProperty("attempts").EnumerateArray());
        Assert.Equal("PermanentFailure", attempt.GetProperty("outcome").GetString());
        Assert.Equal("DemoPermanentFailure", attempt.GetProperty("errorType").GetString());
    }

    [Fact]
    public async Task A_duplicate_message_for_a_succeeded_job_is_acked_without_a_second_attempt()
    {
        Guid jobId = await SubmitAsync("demo.success", new { });
        await WaitForStatusAsync(jobId, "Succeeded");

        await PublishRawAsync(JsonSerializer.SerializeToUtf8Bytes(
            new { jobId, type = "demo.success", correlationId = "duplicate" }));
        Guid marker = await SubmitAsync("demo.success", new { });
        await WaitForStatusAsync(marker, "Succeeded");
        await StopWorkerAsync();

        JsonElement job = await GetJobAsync(jobId);
        Assert.Single(job.GetProperty("attempts").EnumerateArray());
        Assert.Equal(0u, await ReadyMessageCountAsync());
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

        JsonElement job = await WaitForStatusAsync(pending.Id, "Succeeded");
        Assert.Single(job.GetProperty("attempts").EnumerateArray());
    }

    [Fact]
    public async Task An_unreadable_message_is_rejected_and_does_not_block_the_queue()
    {
        await PublishRawAsync(Encoding.UTF8.GetBytes("not json"));

        Guid marker = await SubmitAsync("data.process", new { rows = 3 });
        await WaitForStatusAsync(marker, "Succeeded");
        await StopWorkerAsync();

        Assert.Equal(0u, await ReadyMessageCountAsync());
    }

    private async Task<Guid> SubmitAsync(string type, object payload)
    {
        HttpResponseMessage response = await _client.PostAsJsonAsync("/api/v1/jobs", new { type, payload });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        JsonElement body = await response.Content.ReadFromJsonAsync<JsonElement>();
        return body.GetProperty("id").GetGuid();
    }

    private async Task<JsonElement> GetJobAsync(Guid jobId)
    {
        return await _client.GetFromJsonAsync<JsonElement>($"/api/v1/jobs/{jobId}");
    }

    private async Task<JsonElement> WaitForStatusAsync(Guid jobId, string status)
    {
        DateTime deadline = DateTime.UtcNow + WaitLimit;
        JsonElement job;
        do
        {
            job = await GetJobAsync(jobId);
            if (job.GetProperty("status").GetString() == status)
            {
                return job;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(200));
        }
        while (DateTime.UtcNow < deadline);

        Assert.Fail($"Job {jobId} stayed {job.GetProperty("status").GetString()} instead of reaching {status}.");
        return job;
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

    private async Task<uint> ReadyMessageCountAsync()
    {
        await using IConnection connection = await _rabbit.ConnectAsync();
        await using IChannel channel = await connection.CreateChannelAsync();
        QueueDeclareOk queue = await channel.QueueDeclarePassiveAsync(RabbitMqTopology.ProcessQueue);
        return queue.MessageCount;
    }

    private async Task StopWorkerAsync()
    {
        if (_worker is null)
        {
            return;
        }

        await _worker.StopAsync();
        _worker.Dispose();
        _worker = null;
    }
}
