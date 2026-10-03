using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;

using TaskFlow.Infrastructure.Messaging;
using TaskFlow.Infrastructure.Persistence;

using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;

using RabbitMQ.Client;

namespace TaskFlow.IntegrationTests;

[Collection(InfrastructureCollection.Name)]
public sealed class JobPublishingTests
{
    private readonly SqlServerFixture _sql;
    private readonly RabbitMqFixture _rabbit;

    public JobPublishingTests(SqlServerFixture sql, RabbitMqFixture rabbit)
    {
        _sql = sql;
        _rabbit = rabbit;
    }

    [Fact]
    public async Task Post_publishes_a_persistent_message_that_carries_only_the_job_reference()
    {
        await using IConnection connection = await _rabbit.ConnectAsync();
        await using IChannel channel = await connection.CreateChannelAsync();
        await RabbitMqTopology.DeclareAsync(channel, CancellationToken.None);
        await channel.QueuePurgeAsync(RabbitMqTopology.ProcessQueue);

        using WebApplicationFactory<Program> factory = CreateFactory(_rabbit.ConnectionString);
        using HttpClient client = factory.CreateClient();
        HttpResponseMessage created = await client.PostAsJsonAsync("/api/v1/jobs", new
        {
            type = "email.send",
            payload = new { to = "someone@example.com", secret = "do-not-ship" },
            correlationId = "publish-1"
        });

        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        JsonElement job = await created.Content.ReadFromJsonAsync<JsonElement>();
        Guid jobId = job.GetProperty("id").GetGuid();
        Assert.Equal("Queued", job.GetProperty("status").GetString());

        BasicGetResult? delivered = await channel.BasicGetAsync(RabbitMqTopology.ProcessQueue, autoAck: true);
        Assert.NotNull(delivered);
        Assert.Equal(RabbitMqTopology.Exchange, delivered.Exchange);
        Assert.Equal(RabbitMqTopology.ProcessRoutingKey, delivered.RoutingKey);
        Assert.True(delivered.BasicProperties.Persistent);
        Assert.Equal("application/json", delivered.BasicProperties.ContentType);
        Assert.Equal(jobId.ToString(), delivered.BasicProperties.MessageId);
        Assert.Equal("publish-1", delivered.BasicProperties.CorrelationId);

        string raw = Encoding.UTF8.GetString(delivered.Body.Span);
        using JsonDocument body = JsonDocument.Parse(raw);
        Assert.Equal(jobId, body.RootElement.GetProperty("jobId").GetGuid());
        Assert.Equal("email.send", body.RootElement.GetProperty("type").GetString());
        Assert.Equal("publish-1", body.RootElement.GetProperty("correlationId").GetString());
        Assert.Equal(3, body.RootElement.EnumerateObject().Count());
        Assert.DoesNotContain("do-not-ship", raw, StringComparison.Ordinal);

        HttpResponseMessage fetched = await client.GetAsync($"/api/v1/jobs/{jobId}");
        JsonElement stored = await fetched.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Queued", stored.GetProperty("status").GetString());
    }

    [Fact]
    public async Task First_publish_declares_the_exchange_queue_and_binding()
    {
        await using IConnection connection = await _rabbit.ConnectAsync();
        await using (IChannel cleanup = await connection.CreateChannelAsync())
        {
            await cleanup.QueueDeleteAsync(RabbitMqTopology.ProcessQueue);
            await cleanup.ExchangeDeleteAsync(RabbitMqTopology.Exchange);
        }

        using WebApplicationFactory<Program> factory = CreateFactory(_rabbit.ConnectionString);
        using HttpClient client = factory.CreateClient();
        HttpResponseMessage created = await client.PostAsJsonAsync("/api/v1/jobs", new
        {
            type = "demo.success",
            payload = new { }
        });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);

        await using IChannel channel = await connection.CreateChannelAsync();
        await channel.ExchangeDeclarePassiveAsync(RabbitMqTopology.Exchange);
        QueueDeclareOk queue = await channel.QueueDeclarePassiveAsync(RabbitMqTopology.ProcessQueue);
        Assert.Equal(1u, queue.MessageCount);
    }

    [Fact]
    public async Task Unreachable_broker_returns_503_and_leaves_the_stored_job_pending()
    {
        using WebApplicationFactory<Program> factory = CreateFactory(
            "amqp://guest:guest@127.0.0.1:1/",
            builder => builder.UseSetting("TaskFlow:RabbitMq:PublishTimeout", "00:00:03"));
        using HttpClient client = factory.CreateClient();

        HttpResponseMessage response = await client.PostAsJsonAsync("/api/v1/jobs", new
        {
            type = "demo.success",
            payload = new { message = "hello" }
        });

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        JsonElement problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("RabbitMQ is unavailable.", problem.GetProperty("title").GetString());
        Assert.Equal(503, problem.GetProperty("status").GetInt32());
        Assert.Equal("Pending", problem.GetProperty("jobStatus").GetString());
        Guid jobId = problem.GetProperty("jobId").GetGuid();

        HttpResponseMessage fetched = await client.GetAsync($"/api/v1/jobs/{jobId}");
        Assert.Equal(HttpStatusCode.OK, fetched.StatusCode);
        JsonElement stored = await fetched.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Pending", stored.GetProperty("status").GetString());
    }

    [Fact]
    public async Task Missing_rabbitmq_setting_returns_503_without_storing_a_job()
    {
        int before = await CountJobsAsync();

        using WebApplicationFactory<Program> factory = CreateFactory(rabbitConnectionString: null);
        using HttpClient client = factory.CreateClient();
        HttpResponseMessage response = await client.PostAsJsonAsync("/api/v1/jobs", new
        {
            type = "demo.success",
            payload = new { message = "hello" }
        });

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        JsonElement problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("RabbitMQ is not configured.", problem.GetProperty("title").GetString());
        Assert.Equal(before, await CountJobsAsync());

        HttpResponseMessage list = await client.GetAsync("/api/v1/jobs");
        Assert.Equal(HttpStatusCode.OK, list.StatusCode);
    }

    private WebApplicationFactory<Program> CreateFactory(
        string? rabbitConnectionString,
        Action<IWebHostBuilder>? configure = null)
    {
        return new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseSetting("ConnectionStrings:TaskFlow", _sql.ConnectionString);
            if (rabbitConnectionString is not null)
            {
                builder.UseSetting("ConnectionStrings:RabbitMq", rabbitConnectionString);
            }

            configure?.Invoke(builder);
        });
    }

    private async Task<int> CountJobsAsync()
    {
        await using TaskFlowDbContext db = _sql.CreateContext();
        return await db.Jobs.CountAsync();
    }
}
