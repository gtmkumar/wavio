using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using RabbitMQ.Client;

namespace WaAdmin.Infrastructure.Messaging;

/// <summary>
/// Lazily creates and caches a single RabbitMQ connection for the host (registered Singleton),
/// reconnecting on demand when the cached connection has dropped. Same shape as WaIngest's
/// <c>RabbitMqConnectionManager</c> — duplicated rather than shared, matching that project's
/// existing convention of each bounded context owning its own thin messaging layer.
/// </summary>
public sealed partial class RabbitMqConnectionManager : IAsyncDisposable
{
    private const string DevelopmentOnlyFallback = "amqp://guest:guest@localhost:5672";

    private readonly string _connectionString;
    private readonly ILogger<RabbitMqConnectionManager> _logger;
    private readonly SemaphoreSlim _connectLock = new(1, 1);

    private IConnection? _connection;

    /// <summary>Fails closed outside Development when unconfigured — never silently talks to a
    /// would-be-default local broker in production. WaAdmin.WebApi's Program.cs performs the same
    /// check eagerly at boot.</summary>
    public RabbitMqConnectionManager(
        IConfiguration configuration, IHostEnvironment environment, ILogger<RabbitMqConnectionManager> logger)
    {
        var configured = configuration.GetConnectionString("RabbitMq");

        if (string.IsNullOrWhiteSpace(configured))
        {
            if (!environment.IsDevelopment())
                throw new InvalidOperationException(
                    "ConnectionStrings:RabbitMq is required outside Development. Provide it via " +
                    "ConnectionStrings__RabbitMq env var or a secrets provider. Wavio will NOT start without it.");

            configured = DevelopmentOnlyFallback;
        }

        _connectionString = configured;
        _logger = logger;
    }

    public async Task<IChannel> CreateChannelAsync(CancellationToken cancellationToken)
    {
        var connection = await GetConnectionAsync(cancellationToken);
        return await connection.CreateChannelAsync(cancellationToken: cancellationToken);
    }

    private async Task<IConnection> GetConnectionAsync(CancellationToken cancellationToken)
    {
        if (_connection is { IsOpen: true } open) return open;

        await _connectLock.WaitAsync(cancellationToken);
        try
        {
            if (_connection is { IsOpen: true } stillOpen) return stillOpen;

            if (_connection is not null)
            {
                try { await _connection.CloseAsync(cancellationToken); }
                catch { /* already broken — closing best-effort only */ }
                _connection.Dispose();
            }

            var factory = new ConnectionFactory
            {
                Uri = new Uri(_connectionString),
                RequestedConnectionTimeout = TimeSpan.FromSeconds(5)
            };

            _connection = await factory.CreateConnectionAsync(cancellationToken);
            LogConnected(_logger);
            return _connection;
        }
        finally
        {
            _connectLock.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_connection is null) return;
        try { await _connection.CloseAsync(); }
        catch { /* shutting down — best effort */ }
        _connection.Dispose();
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Connected to RabbitMQ")]
    private static partial void LogConnected(ILogger logger);
}
