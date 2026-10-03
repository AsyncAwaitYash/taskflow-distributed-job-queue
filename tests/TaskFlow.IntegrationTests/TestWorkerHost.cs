using TaskFlow.Application;
using TaskFlow.Infrastructure;
using TaskFlow.Infrastructure.Messaging;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

using RabbitMQ.Client;

namespace TaskFlow.IntegrationTests;

/// <summary>
/// Builds a worker host with the same registration calls as <c>TaskFlow.Worker/Program.cs</c>.
/// Each host has its own RabbitMQ connection, so two hosts are two separate consumers.
/// </summary>
internal static class TestWorkerHost
{
    public static async Task<IHost> StartAsync(SqlServerFixture sql, RabbitMqFixture rabbit, string workerId)
    {
        HostApplicationBuilder builder = Host.CreateApplicationBuilder();
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:TaskFlow"] = sql.ConnectionString,
            ["ConnectionStrings:RabbitMq"] = rabbit.ConnectionString,
            ["TaskFlow:Worker:WorkerId"] = workerId
        });
        builder.Services.AddTaskFlowApplication(builder.Configuration);
        TaskFlowInfrastructureStatus infrastructure = builder.Services.AddTaskFlowInfrastructure(builder.Configuration, $"taskflow-{workerId}");
        builder.Services.AddTaskFlowJobConsumer(builder.Configuration, infrastructure);

        IHost host = builder.Build();
        await host.StartAsync();
        return host;
    }

    public static async Task StopAsync(IHost? host)
    {
        if (host is null)
        {
            return;
        }

        await host.StopAsync();
        host.Dispose();
    }

    /// <summary>
    /// <c>StartAsync</c> returns before the consumer is registered. Submitting earlier would let one worker take everything.
    /// </summary>
    public static async Task WaitForConsumersAsync(RabbitMqFixture rabbit, uint expected)
    {
        DateTime deadline = DateTime.UtcNow + TimeSpan.FromSeconds(30);
        uint current = 0;
        while (DateTime.UtcNow < deadline)
        {
            current = (await DeclarePassiveAsync(rabbit)).ConsumerCount;
            if (current >= expected)
            {
                return;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(100));
        }

        Assert.Fail($"Expected {expected} consumers on {RabbitMqTopology.ProcessQueue}, saw {current}.");
    }

    public static async Task<uint> ReadyMessageCountAsync(RabbitMqFixture rabbit)
    {
        return (await DeclarePassiveAsync(rabbit)).MessageCount;
    }

    public static async Task PurgeAsync(RabbitMqFixture rabbit)
    {
        await using IConnection connection = await rabbit.ConnectAsync();
        await using IChannel channel = await connection.CreateChannelAsync();
        await RabbitMqTopology.DeclareAsync(channel, CancellationToken.None);
        await channel.QueuePurgeAsync(RabbitMqTopology.ProcessQueue);
    }

    private static async Task<QueueDeclareOk> DeclarePassiveAsync(RabbitMqFixture rabbit)
    {
        await using IConnection connection = await rabbit.ConnectAsync();
        await using IChannel channel = await connection.CreateChannelAsync();

        // The queue may not exist yet if no worker has connected since a test deleted it.
        await RabbitMqTopology.DeclareAsync(channel, CancellationToken.None);
        return await channel.QueueDeclarePassiveAsync(RabbitMqTopology.ProcessQueue);
    }
}
