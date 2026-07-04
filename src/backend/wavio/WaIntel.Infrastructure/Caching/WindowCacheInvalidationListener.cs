using WaIntel.Application.Windows.Queries.GetWindowState;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace WaIntel.Infrastructure.Caching;

/// <summary>
/// Fast-lookup cache invalidation across instances (issue #15, "DocSlot RBAC resolver pattern"):
/// each WaIntel instance opens its own dedicated Postgres connection and LISTENs on
/// <c>conversation_window_changed</c>. <c>UpsertWindowOnMessageReceivedHandler</c> /
/// <c>SimulateWindowHandler</c> NOTIFY that channel (via <c>IWaIntelDbContext.NotifyAsync</c>)
/// with the payload <c>"{tenantId}:{phoneNumberId}:{userWaId}"</c> right after committing a
/// window change. This listener parses out tenantId/userWaId (matching
/// <see cref="GetWindowStateHandler.BuildCacheKey"/> exactly — phone_number_id is deliberately
/// not part of the cache key, see that class) and evicts the local <see cref="IMemoryCache"/>
/// entry — every instance's cache converges within one NOTIFY round-trip, not the 5-minute TTL.
///
/// Uses a raw dedicated connection rather than the pooled EF connection: Npgsql's LISTEN support
/// requires holding one connection open indefinitely and polling/waiting for notifications on it,
/// which is fundamentally incompatible with EF's pooled, short-lived-per-request connection model.
/// </summary>
public sealed partial class WindowCacheInvalidationListener : BackgroundService
{
    public const string ChannelName = "conversation_window_changed";

    private readonly string _connectionString;
    private readonly IMemoryCache _cache;
    private readonly ILogger<WindowCacheInvalidationListener> _logger;

    public WindowCacheInvalidationListener(
        IConfiguration configuration, IMemoryCache cache, ILogger<WindowCacheInvalidationListener> logger)
    {
        _connectionString = configuration.GetConnectionString("Default")
            ?? "Host=localhost;Port=5432;Database=waplatform;Username=app_user;Password=app_user";
        _cache = cache;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await using var connection = new NpgsqlConnection(_connectionString);
                await connection.OpenAsync(stoppingToken);

                connection.Notification += (_, notification) => OnNotification(notification.Payload);

                await using (var listenCommand = new NpgsqlCommand($"LISTEN {ChannelName}", connection))
                {
                    await listenCommand.ExecuteNonQueryAsync(stoppingToken);
                }

                LogListening(_logger);

                // Npgsql only surfaces notifications while actively waiting on the connection —
                // poll WaitAsync in a loop for the lifetime of this connection.
                while (!stoppingToken.IsCancellationRequested)
                {
                    await connection.WaitAsync(stoppingToken);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                // Connection dropped — log and reconnect rather than let the whole listener die;
                // a gap here only means the TTL (5 min) is the fallback until reconnected.
                LogConnectionLost(_logger, ex);
                await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
            }
        }
    }

    private void OnNotification(string payload)
    {
        // Payload: "{tenantId}:{phoneNumberId}:{userWaId}" — reconstruct the cache key from just
        // the first and last segments (phoneNumberId is a GUID with no colons, so a 3-way split
        // on the first two colons is unambiguous even though userWaId itself never contains one).
        var firstColon = payload.IndexOf(':');
        var lastColon = payload.LastIndexOf(':');
        if (firstColon < 0 || lastColon <= firstColon)
        {
            LogMalformedPayload(_logger, payload);
            return;
        }

        var tenantIdText = payload[..firstColon];
        var userWaId = payload[(lastColon + 1)..];

        if (!Guid.TryParse(tenantIdText, out var tenantId))
        {
            LogMalformedPayload(_logger, payload);
            return;
        }

        _cache.Remove(GetWindowStateHandler.BuildCacheKey(tenantId, userWaId));
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Listening for window-change notifications")]
    private static partial void LogListening(ILogger logger);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Window cache invalidation listener lost its connection — reconnecting")]
    private static partial void LogConnectionLost(ILogger logger, Exception exception);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Malformed window-change notification payload: {Payload}")]
    private static partial void LogMalformedPayload(ILogger logger, string payload);
}
