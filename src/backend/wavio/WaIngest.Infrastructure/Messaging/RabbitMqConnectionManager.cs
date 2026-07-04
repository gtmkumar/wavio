using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using RabbitMQ.Client;

namespace WaIngest.Infrastructure.Messaging;

/// <summary>
/// Lazily creates and caches a single RabbitMQ connection for the host (registered Singleton),
/// reconnecting on demand when the cached connection has dropped. Deliberately does NOT retry in a
/// loop or block for long: a broker outage must surface as a fast exception so
/// <c>WebhookProcessor</c> can mark the raw webhook row 'failed' and move on (spec §8 degraded
/// mode — the ack path must never be held up waiting on the bus, and neither should the
/// background worker stall indefinitely on one connection attempt).
/// </summary>
public sealed partial class RabbitMqConnectionManager : IAsyncDisposable
{
    private const string DevelopmentOnlyFallback = "amqp://guest:guest@localhost:5672";

    private readonly string _connectionString;
    private readonly ILogger<RabbitMqConnectionManager> _logger;
    private readonly SemaphoreSlim _connectLock = new(1, 1);

    private IConnection? _connection;

    /// <summary>
    /// Fails closed outside Development when unconfigured — mirrors the Meta:Webhook /
    /// Pii:EncryptionKey startup posture (never silently talk to a would-be-default local
    /// broker in production). WaIngest.WebApi's Program.cs already performs the same check
    /// eagerly at boot; this is the second, independent layer for any other composition root
    /// that constructs this class without that guard.
    /// </summary>
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
                // Bound the connect attempt — a hanging broker must not stall the caller.
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
