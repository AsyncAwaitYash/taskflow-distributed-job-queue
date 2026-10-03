using Microsoft.Extensions.Options;

using RabbitMQ.Client;

namespace TaskFlow.Infrastructure.Messaging;

/// <summary>
/// Owns the single AMQP connection for the process. The connection is opened on first use,
/// so the host starts even when RabbitMQ is down.
/// </summary>
internal sealed class RabbitMqConnectionProvider : IAsyncDisposable
{
    private readonly ConnectionFactory _factory;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private IConnection? _connection;

    public RabbitMqConnectionProvider(RabbitMqEndpoint endpoint, IOptions<RabbitMqOptions> options)
    {
        _factory = new ConnectionFactory
        {
            Uri = endpoint.Uri,
            AutomaticRecoveryEnabled = true,
            TopologyRecoveryEnabled = true,
            RequestedConnectionTimeout = options.Value.PublishTimeout,
            ClientProvidedName = endpoint.ClientName
        };
    }

    public async Task<IConnection> GetConnectionAsync(CancellationToken cancellationToken)
    {
        IConnection? current = _connection;
        if (current is { IsOpen: true })
        {
            return current;
        }

        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (_connection is { IsOpen: true })
            {
                return _connection;
            }

            if (_connection is not null)
            {
                await _connection.DisposeAsync();
                _connection = null;
            }

            IConnection connection = await _factory.CreateConnectionAsync(cancellationToken);
            try
            {
                await using IChannel channel = await connection.CreateChannelAsync(cancellationToken: cancellationToken);
                await RabbitMqTopology.DeclareAsync(channel, cancellationToken);
            }
            catch
            {
                await connection.DisposeAsync();
                throw;
            }

            _connection = connection;
            return connection;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_connection is not null)
        {
            await _connection.DisposeAsync();
            _connection = null;
        }

        _gate.Dispose();
    }
}

internal sealed record RabbitMqEndpoint(Uri Uri, string ClientName);
