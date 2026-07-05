using System.Text.Json;
using wavio.Utilities.Auth.Audit;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace WaAdmin.Infrastructure.BackgroundWork;

/// <summary>
/// Processes pending <c>consent.erasure_requests</c> (issue #21, spec §4.10/§9). Same cross-tenant
/// scan shape as WaIntel's <c>HealthSnapshotRollupService</c>: list outstanding work on the
/// privileged Admin connection (the ONLY thing it's used for), then do the actual read/write for
/// each request on a normal app connection with <c>app.tenant_id</c> explicitly set, so RLS
/// applies exactly as it would for any tenant-scoped request.
///
/// Claiming without a lease token: <c>erasure_requests</c> has no <c>locked_by</c>/<c>locked_at</c>
/// column (the schema is frozen, V012) — this worker instead claims via a single conditional
/// <c>UPDATE ... SET status='processing' WHERE id=@id AND status='pending'</c> and only proceeds
/// if that affects exactly one row. Acceptable for this workload (erasure/export requests are
/// rare, human-triggered events, not a high-throughput queue) — a genuinely concurrent second
/// instance racing the SAME request id still can't double-process it, it just loses the claim.
///
/// ERASURE scope: blanks the <c>payload</c> column on <c>messaging.outbound_messages</c>
/// (to_wa_id match) and <c>messaging.inbound_messages</c> (from_wa_id match) for this
/// (tenant, wa_id) — message CONTENT only. Every other column (ids, wamid, status, timestamps)
/// is left intact so correlation/audit still works, and <c>billing.message_costs</c> is NEVER
/// touched (it has no wa_id column at all — see db/migrations/V010 — so "preserve the cost
/// ledger" requires no special-casing here, it is simply a different schema this worker never
/// writes to).
///
/// EXPORT scope (v1, pragmatic): collects the principal's MESSAGE METADATA (not message content —
/// see the class's own erasure-scope note above for why content isn't duplicated into an export
/// artifact in Wave 1), consent events, and cost-ledger entries into a JSON payload written to a
/// local file under <see cref="_exportDirectory"/>; <c>export_ref</c> stores that file path. A
/// real deployment would put this in object storage with a signed URL — a local path is
/// documented here as the v1 shortcut, not a design endorsement.
/// </summary>
public sealed partial class ErasureRequestProcessorService : BackgroundService
{
    private readonly string _adminConnectionString;
    private readonly string _appConnectionString;
    private readonly string _exportDirectory;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly TimeSpan _interval;
    private readonly ILogger<ErasureRequestProcessorService> _logger;

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    public ErasureRequestProcessorService(
        IConfiguration configuration, IServiceScopeFactory scopeFactory, ILogger<ErasureRequestProcessorService> logger)
    {
        _adminConnectionString = configuration.GetConnectionString("Admin")
            ?? throw new InvalidOperationException("ConnectionStrings:Admin is not configured.");
        _appConnectionString = configuration.GetConnectionString("Default")
            ?? throw new InvalidOperationException("ConnectionStrings:Default is not configured.");
        _exportDirectory = configuration.GetValue("Consent:ExportDirectory", "./data/consent-exports")!;
        _scopeFactory = scopeFactory;

        // Polled frequently — erasure/export requests are rare and each one is a compliance
        // deadline for the tenant, so a short poll interval (default 30s) is cheap and keeps
        // turnaround fast; there is no ON-CONFLICT-style claim guard doing the heavy lifting here
        // the way there is in HealthSnapshotRollupService, so a shorter interval also narrows the
        // (already-fenced) claim race window.
        _interval = TimeSpan.FromSeconds(configuration.GetValue("Consent:ErasureProcessor:IntervalSeconds", 30));

        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(_interval);
        do
        {
            try
            {
                await RunOneTickAsync(stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                LogTickFailed(_logger, ex);
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    private async Task RunOneTickAsync(CancellationToken stoppingToken)
    {
        var pending = await ListPendingAsync(stoppingToken);
        foreach (var request in pending)
        {
            await ProcessOneAsync(request, stoppingToken);
        }
    }

    private async Task<List<(Guid Id, Guid TenantId, string WaId, string RequestType)>> ListPendingAsync(
        CancellationToken ct)
    {
        await using var connection = new NpgsqlConnection(_adminConnectionString);
        await connection.OpenAsync(ct);

        await using var command = new NpgsqlCommand(
            "SELECT id, tenant_id, wa_id, request_type FROM consent.erasure_requests WHERE status = 'pending'", connection);
        await using var reader = await command.ExecuteReaderAsync(ct);

        var results = new List<(Guid, Guid, string, string)>();
        while (await reader.ReadAsync(ct))
        {
            results.Add((reader.GetGuid(0), reader.GetGuid(1), reader.GetString(2), reader.GetString(3)));
        }
        return results;
    }

    private async Task ProcessOneAsync((Guid Id, Guid TenantId, string WaId, string RequestType) request, CancellationToken ct)
    {
        await using var connection = new NpgsqlConnection(_appConnectionString);
        await connection.OpenAsync(ct);

        await using (var setGuc = new NpgsqlCommand("SELECT set_config('app.tenant_id', @tenantId, false)", connection))
        {
            setGuc.Parameters.AddWithValue("@tenantId", request.TenantId.ToString());
            await setGuc.ExecuteNonQueryAsync(ct);
        }

        if (!await TryClaimAsync(connection, request.Id, ct))
        {
            return; // another instance/tick already claimed this request
        }

        try
        {
            if (request.RequestType == "erasure")
            {
                await ProcessErasureAsync(connection, request, ct);
            }
            else
            {
                await ProcessExportAsync(connection, request, ct);
            }
            await WriteAuditEntryAsync(request, success: true, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            LogRequestFailed(_logger, request.Id, ex);
            await MarkRejectedAsync(connection, request.Id, ex.Message, ct);
            await WriteAuditEntryAsync(request, success: false, ct);
        }
    }

    private static async Task<bool> TryClaimAsync(NpgsqlConnection connection, Guid requestId, CancellationToken ct)
    {
        await using var command = new NpgsqlCommand(
            """
            UPDATE consent.erasure_requests
            SET status = 'processing', updated_at = now(), version = version + 1
            WHERE id = @id AND status = 'pending'
            """, connection);
        command.Parameters.AddWithValue("@id", requestId);
        var rowsAffected = await command.ExecuteNonQueryAsync(ct);
        return rowsAffected == 1;
    }

    private static async Task ProcessErasureAsync(
        NpgsqlConnection connection, (Guid Id, Guid TenantId, string WaId, string RequestType) request, CancellationToken ct)
    {
        await using (var eraseOutbound = new NpgsqlCommand(
            "UPDATE messaging.outbound_messages SET payload = '{}'::jsonb, updated_at = now() " +
            "WHERE tenant_id = @tenantId AND to_wa_id = @waId", connection))
        {
            eraseOutbound.Parameters.AddWithValue("@tenantId", request.TenantId);
            eraseOutbound.Parameters.AddWithValue("@waId", request.WaId);
            await eraseOutbound.ExecuteNonQueryAsync(ct);
        }

        await using (var eraseInbound = new NpgsqlCommand(
            "UPDATE messaging.inbound_messages SET payload = '{}'::jsonb, context = NULL, referral = NULL " +
            "WHERE tenant_id = @tenantId AND from_wa_id = @waId", connection))
        {
            eraseInbound.Parameters.AddWithValue("@tenantId", request.TenantId);
            eraseInbound.Parameters.AddWithValue("@waId", request.WaId);
            await eraseInbound.ExecuteNonQueryAsync(ct);
        }

        await using var complete = new NpgsqlCommand(
            """
            UPDATE consent.erasure_requests
            SET status = 'completed', content_erased_at = now(), completed_at = now(),
                updated_at = now(), version = version + 1
            WHERE id = @id
            """, connection);
        complete.Parameters.AddWithValue("@id", request.Id);
        await complete.ExecuteNonQueryAsync(ct);
    }

    private async Task ProcessExportAsync(
        NpgsqlConnection connection, (Guid Id, Guid TenantId, string WaId, string RequestType) request, CancellationToken ct)
    {
        var messages = new List<object>();
        await using (var select = new NpgsqlCommand(
            "SELECT wamid, message_type, status, accepted_at, dispatched_at FROM messaging.outbound_messages " +
            "WHERE tenant_id = @tenantId AND to_wa_id = @waId ORDER BY accepted_at", connection))
        {
            select.Parameters.AddWithValue("@tenantId", request.TenantId);
            select.Parameters.AddWithValue("@waId", request.WaId);
            await using var reader = await select.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
            {
                messages.Add(new
                {
                    wamid = reader.IsDBNull(0) ? null : reader.GetString(0),
                    messageType = reader.GetString(1),
                    status = reader.GetString(2),
                    acceptedAt = reader.GetFieldValue<DateTimeOffset>(3),
                    dispatchedAt = reader.IsDBNull(4) ? (DateTimeOffset?)null : reader.GetFieldValue<DateTimeOffset>(4),
                });
            }
        }

        var consentEvents = new List<object>();
        await using (var select = new NpgsqlCommand(
            "SELECT 'opt_in' AS kind, purpose AS detail, occurred_at FROM consent.opt_in_events WHERE tenant_id = @tenantId AND wa_id = @waId " +
            "UNION ALL " +
            "SELECT 'opt_out' AS kind, reason AS detail, occurred_at FROM consent.opt_out_events WHERE tenant_id = @tenantId AND wa_id = @waId " +
            "ORDER BY occurred_at", connection))
        {
            select.Parameters.AddWithValue("@tenantId", request.TenantId);
            select.Parameters.AddWithValue("@waId", request.WaId);
            await using var reader = await select.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
            {
                consentEvents.Add(new
                {
                    kind = reader.GetString(0),
                    detail = reader.GetString(1),
                    occurredAt = reader.GetFieldValue<DateTimeOffset>(2),
                });
            }
        }

        var costs = new List<object>();
        await using (var select = new NpgsqlCommand(
            "SELECT mc.wamid, mc.category, mc.amount, mc.currency, mc.billed_at " +
            "FROM billing.message_costs mc " +
            "JOIN messaging.outbound_messages om ON om.wamid = mc.wamid " +
            "WHERE om.tenant_id = @tenantId AND om.to_wa_id = @waId", connection))
        {
            select.Parameters.AddWithValue("@tenantId", request.TenantId);
            select.Parameters.AddWithValue("@waId", request.WaId);
            await using var reader = await select.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
            {
                costs.Add(new
                {
                    wamid = reader.GetString(0),
                    category = reader.GetString(1),
                    amount = reader.GetDecimal(2),
                    currency = reader.GetString(3),
                    billedAt = reader.GetFieldValue<DateTimeOffset>(4),
                });
            }
        }

        var exportPayload = new
        {
            waId = request.WaId,
            tenantId = request.TenantId,
            exportedAt = DateTimeOffset.UtcNow,
            messagesMetadata = messages,
            consentEvents,
            costs,
        };

        Directory.CreateDirectory(_exportDirectory);
        var fileName = $"export-{request.TenantId:N}-{request.Id:N}.json";
        var filePath = Path.Combine(_exportDirectory, fileName);
        await File.WriteAllTextAsync(filePath, JsonSerializer.Serialize(exportPayload, JsonOptions), ct);

        await using var complete = new NpgsqlCommand(
            """
            UPDATE consent.erasure_requests
            SET status = 'completed', export_ref = @exportRef, completed_at = now(),
                updated_at = now(), version = version + 1
            WHERE id = @id
            """, connection);
        complete.Parameters.AddWithValue("@id", request.Id);
        complete.Parameters.AddWithValue("@exportRef", filePath);
        await complete.ExecuteNonQueryAsync(ct);
    }

    private static async Task MarkRejectedAsync(NpgsqlConnection connection, Guid requestId, string reason, CancellationToken ct)
    {
        await using var command = new NpgsqlCommand(
            """
            UPDATE consent.erasure_requests
            SET status = 'rejected', reason = COALESCE(reason || ' | ', '') || @failureReason,
                updated_at = now(), version = version + 1
            WHERE id = @id
            """, connection);
        command.Parameters.AddWithValue("@id", requestId);
        command.Parameters.AddWithValue("@failureReason", $"processing failed: {reason}");
        await command.ExecuteNonQueryAsync(ct);
    }

    private async Task WriteAuditEntryAsync(
        (Guid Id, Guid TenantId, string WaId, string RequestType) request, bool success, CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var auditWriter = scope.ServiceProvider.GetRequiredService<IAuditWriter>();
        await auditWriter.WriteAsync(
            action: "consent.erasure_request_processed",
            resourceType: "erasure_requests",
            success: success,
            newValues: new { request.Id, request.TenantId, request.RequestType },
            ct: ct);
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Erasure-request processor tick failed")]
    private static partial void LogTickFailed(ILogger logger, Exception exception);

    [LoggerMessage(Level = LogLevel.Error, Message = "Erasure/export request {RequestId} failed to process — marked rejected")]
    private static partial void LogRequestFailed(ILogger logger, Guid requestId, Exception exception);
}
