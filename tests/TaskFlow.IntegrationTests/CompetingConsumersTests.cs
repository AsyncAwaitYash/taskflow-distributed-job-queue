using System.Text.Json;

using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Hosting;

namespace TaskFlow.IntegrationTests;

[Collection(InfrastructureCollection.Name)]
public sealed class CompetingConsumersTests : IAsyncLifetime
{
    private readonly SqlServerFixture _sql;
    private readonly RabbitMqFixture _rabbit;
    private readonly WebApplicationFactory<Program> _api;
    private readonly HttpClient _client;
    private IHost? _workerA;
    private IHost? _workerB;

    public CompetingConsumersTests(SqlServerFixture sql, RabbitMqFixture rabbit)
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
        await TestWorkerHost.PurgeAsync(_rabbit);
        _workerA = await TestWorkerHost.StartAsync(_sql, _rabbit, "worker-a");
        _workerB = await TestWorkerHost.StartAsync(_sql, _rabbit, "worker-b");
        await TestWorkerHost.WaitForConsumersAsync(_rabbit, 2);
    }

    public async Task DisposeAsync()
    {
        await StopWorkersAsync();
        _client.Dispose();
        await _api.DisposeAsync();
    }

    [Fact]
    public async Task Two_workers_share_one_queue_and_each_job_runs_once()
    {
        List<Guid> jobIds = [];
        for (int index = 0; index < 8; index++)
        {
            jobIds.Add(await TestJobApi.SubmitAsync(_client, "demo.slow", new { seconds = 1 }));
        }

        List<JsonElement> jobs = [];
        foreach (Guid jobId in jobIds)
        {
            jobs.Add(await TestJobApi.WaitForStatusAsync(_client, jobId, "Succeeded"));
        }

        List<string> workerIds = jobs
            .Select(job => Assert.Single(job.GetProperty("attempts").EnumerateArray()).GetProperty("workerId").GetString()!)
            .ToList();
        Assert.Equal(["worker-a", "worker-b"], workerIds.Distinct().Order(StringComparer.Ordinal).ToArray());

        await StopWorkersAsync();
        Assert.Equal(0u, await TestWorkerHost.ReadyMessageCountAsync(_rabbit));
    }

    [Fact]
    public async Task End_to_end_happy_path_covers_every_registered_handler()
    {
        (string Type, object Payload, string Expected)[] cases =
        [
            ("demo.success", new { message = "hello" }, "Succeeded"),
            ("demo.transient-failure", new { failTimes = 0 }, "Succeeded"),
            ("demo.slow", new { seconds = 0 }, "Succeeded"),
            ("email.send", new { to = "someone@example.com" }, "Succeeded"),
            ("report.generate", new { name = "daily" }, "Succeeded"),
            ("data.process", new { rows = 10 }, "Succeeded"),
            ("demo.permanent-failure", new { }, "Failed")
        ];

        List<(Guid Id, string Expected)> submitted = [];
        foreach ((string type, object payload, string expected) in cases)
        {
            submitted.Add((await TestJobApi.SubmitAsync(_client, type, payload), expected));
        }

        foreach ((Guid id, string expected) in submitted)
        {
            JsonElement job = await TestJobApi.WaitForStatusAsync(_client, id, expected);
            JsonElement attempt = Assert.Single(job.GetProperty("attempts").EnumerateArray());
            Assert.Equal(JsonValueKind.String, attempt.GetProperty("completedAt").ValueKind);
            Assert.Equal(JsonValueKind.Number, attempt.GetProperty("durationMilliseconds").ValueKind);
            Assert.Contains(attempt.GetProperty("workerId").GetString(), new[] { "worker-a", "worker-b" });
        }
    }

    private async Task StopWorkersAsync()
    {
        await Task.WhenAll(TestWorkerHost.StopAsync(_workerA), TestWorkerHost.StopAsync(_workerB));
        _workerA = null;
        _workerB = null;
    }
}
