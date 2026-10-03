using RabbitMQ.Client;

using Testcontainers.RabbitMq;

namespace TaskFlow.IntegrationTests;

public sealed class RabbitMqFixture : IAsyncLifetime
{
    static RabbitMqFixture()
    {
        Environment.SetEnvironmentVariable("TESTCONTAINERS_RYUK_DISABLED", "true");
    }

    private readonly RabbitMqContainer _container = new RabbitMqBuilder("rabbitmq:4.1")
        .Build();

    public string ConnectionString => _container.GetConnectionString();

    public Task InitializeAsync()
    {
        return _container.StartAsync();
    }

    public Task<IConnection> ConnectAsync()
    {
        ConnectionFactory factory = new() { Uri = new Uri(ConnectionString) };
        return factory.CreateConnectionAsync();
    }

    public async Task DisposeAsync()
    {
        await _container.DisposeAsync();
    }
}
