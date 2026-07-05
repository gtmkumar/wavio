using WaIntel.Application.Quality.Logic;
using wavio.Utilities.Auth.Audit;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace WaIntel.Infrastructure.BackgroundWork;

/// <summary>
/// Weekly per-number health rollup into <c>quality.health_snapshots</c> (issue #20, spec §4.6).
/// Same cross-tenant scan shape as <c>WindowClosingScannerService</c> (issue #15): list tenant ids
/// on the privileged Admin connection (the ONLY thing it's used for), then run every tenant's
/// actual read/write under a normal app-user connection with <c>app.tenant_id</c> explicitly set,
/// so RLS applies exactly as it would for any tenant-scoped request.
///
/// Idempotent by construction, not by a scan-time check: the insert is
/// <c>ON CONFLICT (phone_number_id, period_start) DO NOTHING</c> — the DB's own unique constraint
/// (db/migrations/V011) is the claim guard, so re-running this tick (or two instances racing) for
/// an already-rolled-up week is a no-op, not a duplicate row or an error.
///
/// Metric SOURCE: <c>messaging.outbound_messages</c> (dispatch time) joined to
/// <c>messaging.message_statuses</c> (delivered/read webhook events) — see
/// <c>WaIntel.Application.Quality.Logic.HealthMetricsRules</c> for the rate math and the
/// documented block-proxy-rate limitation.
/// </summary>
public sealed partial class HealthSnapshotRollupService : BackgroundService
{
    private readonly string _adminConnectionString;
    private readonly string _appConnectionString;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly TimeSpan _interval;
    private readonly ILogger<HealthSnapshotRollupService> _logger;

    public HealthSnapshotRollupService(
        IConfiguration configuration,
        IServiceScopeFactory scopeFactory,
        ILogger<HealthSnapshotRollupService> logger)
    {
        _adminConnectionString = configuration.GetConnectionString("Admin")
            ?? throw new InvalidOperationException("ConnectionStrings:Admin is not configured.");
        _appConnectionString = configuration.GetConnectionString("Default")
            ?? throw new InvalidOperationException("ConnectionStrings:Default is not configured.");
        _scopeFactory = scopeFactory;

        // Checked hourly by default, not weekly — the ON CONFLICT claim guard makes a cheap
        // no-op scan harmless, and a shorter interval means a rollup lands soon after the DB clock
        // crosses into a new week rather than waiting up to a full extra week (also lets live
        // verification override this to a short interval instead of waiting out a real week).
        _interval = TimeSpan.FromSeconds(configuration.GetValue("Quality:HealthRollup:IntervalSeconds", 3600));

        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(_interval);
        do
        {
            try
            {
                await RunRollupAsync(stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                LogRollupFailed(_logger, ex);
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    private async Task RunRollupAsync(CancellationToken stoppingToken)
    {
        var (periodStart, periodEnd) = ComputeMostRecentCompletedWeek(DateTimeOffset.UtcNow);
        var tenantIds = await ListTenantIdsAsync(stoppingToken);
        var snapshotsWritten = 0;

        foreach (var tenantId in tenantIds)
        {
            snapshotsWritten += await RollupTenantAsync(tenantId, periodStart, periodEnd, stoppingToken);
        }

        LogRollupComplete(_logger, tenantIds.Count, snapshotsWritten, periodStart.ToString(), periodEnd.ToString());
        await WriteAuditEntryAsync(tenantIds.Count, snapshotsWritten, periodStart, periodEnd, stoppingToken);
    }

    /// <summary>Monday 00:00 UTC through the following Sunday (inclusive), for the most recently
    /// FULLY COMPLETED week relative to <paramref name="now"/> — the in-progress current week is
    /// never rolled up (its numbers are still moving). Internal + static so it's unit-testable
    /// without spinning up the whole service.</summary>
    internal static (DateOnly Start, DateOnly End) ComputeMostRecentCompletedWeek(DateTimeOffset now)
    {
        var today = DateOnly.FromDateTime(now.UtcDateTime);
        var daysSinceMonday = ((int)today.DayOfWeek + 6) % 7; // Monday=0 ... Sunday=6
        var thisWeekMonday = today.AddDays(-daysSinceMonday);
        var lastWeekMonday = thisWeekMonday.AddDays(-7);
        var lastWeekSunday = thisWeekMonday.AddDays(-1);
        return (lastWeekMonday, lastWeekSunday);
    }

    private async Task<List<Guid>> ListTenantIdsAsync(CancellationToken stoppingToken)
    {
        await using var connection = new NpgsqlConnection(_adminConnectionString);
        await connection.OpenAsync(stoppingToken);

        await using var command = new NpgsqlCommand(
            "SELECT id FROM tenancy.tenants WHERE deleted_at IS NULL", connection);
        await using var reader = await command.ExecuteReaderAsync(stoppingToken);

        var ids = new List<Guid>();
        while (await reader.ReadAsync(stoppingToken))
        {
            ids.Add(reader.GetGuid(0));
        }
        return ids;
    }

    private async Task<int> RollupTenantAsync(Guid tenantId, DateOnly periodStart, DateOnly periodEnd, CancellationToken stoppingToken)
    {
        await using var connection = new NpgsqlConnection(_appConnectionString);
        await connection.OpenAsync(stoppingToken);

        await using (var setGuc = new NpgsqlCommand("SELECT set_config('app.tenant_id', @tenantId, false)", connection))
        {
            setGuc.Parameters.AddWithValue("@tenantId", tenantId.ToString());
            await setGuc.ExecuteNonQueryAsync(stoppingToken);
        }

        var rangeStart = periodStart.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
        var rangeEnd = periodEnd.AddDays(1).ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc); // exclusive upper bound

        var rows = new List<(Guid PhoneNumberId, string? Rating, string? Tier, long Sent, long Delivered, long Read, long Failed)>();

        await using (var select = new NpgsqlCommand(
            """
            SELECT pn.id, pn.quality_rating, pn.messaging_tier,
                   count(*) FILTER (WHERE om.dispatched_at IS NOT NULL) AS messages_sent,
                   count(DISTINCT ms_d.id) AS messages_delivered,
                   count(DISTINCT ms_r.id) AS messages_read,
                   count(*) FILTER (WHERE om.status = 'failed') AS messages_failed
            FROM waba.phone_numbers pn
            JOIN messaging.outbound_messages om
                ON om.phone_number_id = pn.id
               AND om.dispatched_at >= @rangeStart AND om.dispatched_at < @rangeEnd
            LEFT JOIN messaging.message_statuses ms_d
                ON ms_d.outbound_message_id = om.id AND ms_d.status = 'delivered'
               AND ms_d.occurred_at >= @rangeStart AND ms_d.occurred_at < @rangeEnd
            LEFT JOIN messaging.message_statuses ms_r
                ON ms_r.outbound_message_id = om.id AND ms_r.status = 'read'
               AND ms_r.occurred_at >= @rangeStart AND ms_r.occurred_at < @rangeEnd
            GROUP BY pn.id, pn.quality_rating, pn.messaging_tier
            """, connection))
        {
            select.Parameters.AddWithValue("@rangeStart", rangeStart);
            select.Parameters.AddWithValue("@rangeEnd", rangeEnd);
            await using var reader = await select.ExecuteReaderAsync(stoppingToken);
            while (await reader.ReadAsync(stoppingToken))
            {
                rows.Add((
                    reader.GetGuid(0),
                    reader.IsDBNull(1) ? null : reader.GetString(1),
                    reader.IsDBNull(2) ? null : reader.GetString(2),
                    reader.GetInt64(3),
                    reader.GetInt64(4),
                    reader.GetInt64(5),
                    reader.GetInt64(6)));
            }
        }

        var written = 0;
        foreach (var row in rows)
        {
            var deliveryRate = HealthMetricsRules.DeliveryRate(row.Sent, row.Delivered);
            var readRate = HealthMetricsRules.ReadRate(row.Delivered, row.Read);
            var blockProxyRate = HealthMetricsRules.BlockProxyRate(row.Sent, row.Failed);
            var canonicalRating = QualityCodes.NormalizeRating(row.Rating);
            var hasTier = QualityCodes.TryNormalizeTier(row.Tier, out var canonicalTier);
            long? headroom = hasTier ? TierRules.HeadroomFor(canonicalTier, row.Sent) : null;

            await using var insert = new NpgsqlCommand(
                """
                INSERT INTO quality.health_snapshots
                    (id, tenant_id, phone_number_id, period_start, period_end,
                     delivery_rate, read_rate, block_proxy_rate, quality_rating, messaging_tier,
                     tier_headroom, messages_sent, messages_delivered, messages_read, messages_failed,
                     created_at)
                VALUES
                    (gen_random_uuid(), @tenantId, @phoneNumberId, @periodStart, @periodEnd,
                     @deliveryRate, @readRate, @blockProxyRate, @qualityRating, @messagingTier,
                     @tierHeadroom, @sent, @delivered, @read, @failed, now())
                ON CONFLICT (phone_number_id, period_start) DO NOTHING
                """, connection);
            insert.Parameters.AddWithValue("@tenantId", tenantId);
            insert.Parameters.AddWithValue("@phoneNumberId", row.PhoneNumberId);
            insert.Parameters.AddWithValue("@periodStart", periodStart);
            insert.Parameters.AddWithValue("@periodEnd", periodEnd);
            insert.Parameters.AddWithValue("@deliveryRate", deliveryRate);
            insert.Parameters.AddWithValue("@readRate", readRate);
            insert.Parameters.AddWithValue("@blockProxyRate", blockProxyRate);
            insert.Parameters.AddWithValue("@qualityRating", (object?)canonicalRating ?? DBNull.Value);
            insert.Parameters.AddWithValue("@messagingTier", (object?)(hasTier ? canonicalTier : null) ?? DBNull.Value);
            insert.Parameters.AddWithValue("@tierHeadroom", (object?)headroom ?? DBNull.Value);
            insert.Parameters.AddWithValue("@sent", row.Sent);
            insert.Parameters.AddWithValue("@delivered", row.Delivered);
            insert.Parameters.AddWithValue("@read", row.Read);
            insert.Parameters.AddWithValue("@failed", row.Failed);

            written += await insert.ExecuteNonQueryAsync(stoppingToken);
        }

        return written;
    }

    private async Task WriteAuditEntryAsync(
        int tenantsScanned, int snapshotsWritten, DateOnly periodStart, DateOnly periodEnd, CancellationToken stoppingToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var auditWriter = scope.ServiceProvider.GetRequiredService<IAuditWriter>();

        await auditWriter.WriteAsync(
            action: "quality.health_snapshot_rollup",
            resourceType: "health_snapshots",
            success: true,
            newValues: new { tenantsScanned, snapshotsWritten, periodStart, periodEnd },
            ct: stoppingToken);
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Health-snapshot rollup complete: {TenantCount} tenant(s), {SnapshotCount} snapshot(s) written for {PeriodStart}..{PeriodEnd}")]
    private static partial void LogRollupComplete(ILogger logger, int tenantCount, int snapshotCount, string periodStart, string periodEnd);

    [LoggerMessage(Level = LogLevel.Error, Message = "Health-snapshot rollup failed")]
    private static partial void LogRollupFailed(ILogger logger, Exception exception);
}
