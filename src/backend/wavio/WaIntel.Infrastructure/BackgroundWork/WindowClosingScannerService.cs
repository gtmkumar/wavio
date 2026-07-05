using WaIntel.Application.Common.Interfaces;
using WaPlatform.Contracts.IntegrationEvents.V1;
using wavio.Utilities.Auth.Audit;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace WaIntel.Infrastructure.BackgroundWork;

/// <summary>
/// Emits <c>wa.window.closing.v1</c> for windows expiring within the configured horizon (spec
/// §4.5, default 2h — <c>Windows:ClosingScan:HorizonMinutes</c>, override for tests so a live
/// verification doesn't have to wait 2 real hours). Runs every
/// <c>Windows:ClosingScan:IntervalSeconds</c> (default 300s, also overridable).
///
/// CROSS-TENANT READ (the architect's handoff names two options — this uses per-tenant GUC
/// iteration, not a platform_admin-granted app_user, because db/README.md's roles table is
/// explicit that app_user must NEVER be granted platform_admin membership; introducing a new
/// dedicated login role for just this scan felt like more surface area than the problem needed):
///   1. List tenant ids using the Admin (superuser) connection — the ONLY thing this scan uses
///      the superuser connection for; it never touches window data with it.
///   2. For each tenant, on a normal app_user connection, `SET app.tenant_id` to that tenant and
///      run the scan/claim queries under normal RLS — the window read itself is never privileged.
///   3. Write one system.audit_log row per scan cycle (tenant_id NULL — the nullable-tenant RLS
///      pattern explicitly allows this) recording how many tenants were scanned and how many
///      notifications were emitted, satisfying "platform_admin-path usage must audit-log" in
///      spirit even though this path doesn't use the platform_admin role at all.
///
/// The claim-then-publish ordering mirrors WaIngest's WebhookProcessor: SELECT candidates →
/// publish → UPDATE closing_notified_at. A crash between publish and the UPDATE risks a duplicate
/// event on the next scan, never a lost one — consumers must already be idempotent on EventId
/// (IntegrationEvent contract rule), so a duplicate is safe.
/// </summary>
public sealed partial class WindowClosingScannerService : BackgroundService
{
    private readonly string _adminConnectionString;
    private readonly string _appConnectionString;
    private readonly IEventBusPublisher _publisher;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly TimeSpan _interval;
    private readonly TimeSpan _horizon;
    private readonly ILogger<WindowClosingScannerService> _logger;

    public WindowClosingScannerService(
        IConfiguration configuration,
        IEventBusPublisher publisher,
        IServiceScopeFactory scopeFactory,
        ILogger<WindowClosingScannerService> logger)
    {
        _adminConnectionString = configuration.GetConnectionString("Admin")
            ?? throw new InvalidOperationException("ConnectionStrings:Admin is not configured.");
        _appConnectionString = configuration.GetConnectionString("Default")
            ?? throw new InvalidOperationException("ConnectionStrings:Default is not configured.");
        _publisher = publisher;
        _scopeFactory = scopeFactory;

        _interval = TimeSpan.FromSeconds(configuration.GetValue("Windows:ClosingScan:IntervalSeconds", 300));
        _horizon = TimeSpan.FromMinutes(configuration.GetValue("Windows:ClosingScan:HorizonMinutes", 120));

        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(_interval);
        do
        {
            try
            {
                await RunScanAsync(stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                LogScanFailed(_logger, ex);
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    private async Task RunScanAsync(CancellationToken stoppingToken)
    {
        var tenantIds = await ListTenantIdsAsync(stoppingToken);
        var notificationsEmitted = 0;

        foreach (var tenantId in tenantIds)
        {
            notificationsEmitted += await ScanTenantAsync(tenantId, stoppingToken);
        }

        LogScanComplete(_logger, tenantIds.Count, notificationsEmitted);
        await WriteAuditEntryAsync(tenantIds.Count, notificationsEmitted, stoppingToken);
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

    private async Task<int> ScanTenantAsync(Guid tenantId, CancellationToken stoppingToken)
    {
        await using var connection = new NpgsqlConnection(_appConnectionString);
        await connection.OpenAsync(stoppingToken);

        await using (var setGuc = new NpgsqlCommand("SELECT set_config('app.tenant_id', @tenantId, false)", connection))
        {
            setGuc.Parameters.AddWithValue("@tenantId", tenantId.ToString());
            await setGuc.ExecuteNonQueryAsync(stoppingToken);
        }

        var candidates = new List<(Guid Id, string WaId, string MetaPhoneNumberId, string WindowType, DateTimeOffset ExpiresAt)>();

        await using (var select = new NpgsqlCommand(
            """
            SELECT cw.id, cw.user_wa_id, pn.meta_phone_number_id,
                   CASE WHEN cw.cs_expires_at IS NOT NULL AND cw.cs_expires_at <= now() + @horizon
                        THEN 'customer_service' ELSE 'ctwa' END AS window_type,
                   COALESCE(
                       CASE WHEN cw.cs_expires_at IS NOT NULL AND cw.cs_expires_at <= now() + @horizon
                            THEN cw.cs_expires_at END,
                       cw.ctwa_expires_at) AS expires_at
            FROM sessions.conversation_windows cw
            JOIN waba.phone_numbers pn ON pn.id = cw.phone_number_id
            WHERE cw.closing_notified_at IS NULL
              AND (
                    (cw.cs_expires_at IS NOT NULL AND cw.cs_expires_at <= now() + @horizon)
                 OR (cw.ctwa_expires_at IS NOT NULL AND cw.ctwa_expires_at <= now() + @horizon)
              )
            """, connection))
        {
            select.Parameters.AddWithValue("@horizon", _horizon);
            await using var reader = await select.ExecuteReaderAsync(stoppingToken);
            while (await reader.ReadAsync(stoppingToken))
            {
                candidates.Add((
                    reader.GetGuid(0),
                    reader.GetString(1),
                    reader.GetString(2),
                    reader.GetString(3),
                    reader.GetFieldValue<DateTimeOffset>(4)));
            }
        }

        var emitted = 0;
        foreach (var candidate in candidates)
        {
            await _publisher.PublishAsync(
                new WindowClosingV1
                {
                    TenantId = tenantId,
                    WaId = candidate.WaId,
                    PhoneNumberId = candidate.MetaPhoneNumberId,
                    ExpiresAt = candidate.ExpiresAt,
                    WindowType = candidate.WindowType
                },
                stoppingToken);

            await using var claim = new NpgsqlCommand(
                "UPDATE sessions.conversation_windows SET closing_notified_at = now() WHERE id = @id AND closing_notified_at IS NULL",
                connection);
            claim.Parameters.AddWithValue("@id", candidate.Id);
            await claim.ExecuteNonQueryAsync(stoppingToken);

            emitted++;
        }

        return emitted;
    }

    private async Task WriteAuditEntryAsync(int tenantsScanned, int notificationsEmitted, CancellationToken stoppingToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var auditWriter = scope.ServiceProvider.GetRequiredService<IAuditWriter>();

        await auditWriter.WriteAsync(
            action: "sessions.window_closing_scan",
            resourceType: "conversation_windows",
            success: true,
            newValues: new { tenantsScanned, notificationsEmitted },
            ct: stoppingToken);
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Window-closing scan complete: {TenantCount} tenant(s), {NotificationCount} notification(s) emitted")]
    private static partial void LogScanComplete(ILogger logger, int tenantCount, int notificationCount);

    [LoggerMessage(Level = LogLevel.Error, Message = "Window-closing scan failed")]
    private static partial void LogScanFailed(ILogger logger, Exception exception);
}
