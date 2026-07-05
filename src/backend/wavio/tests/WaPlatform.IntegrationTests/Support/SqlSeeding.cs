using Npgsql;

namespace WaPlatform.IntegrationTests.Support;

/// <summary>
/// Raw-Npgsql seed helpers for the FK chains these tests need to exist before their real handlers
/// run (tenancy.tenants -> waba.business_accounts -> waba.phone_numbers). Deliberately NOT EF Core
/// / NOT through any service's DbContext adapter: these rows are test fixtures, not the behavior
/// under test, and — critically — tenancy.tenants' own RLS policy
/// (<c>id = app.current_tenant_id() OR app.is_platform_admin()</c>, db/migrations/V001__tenancy.sql)
/// would reject an app_user-connection INSERT of a brand-new tenant row anyway (nothing has set
/// the GUC to that not-yet-existing tenant's id). Uses the Admin (postgres superuser) connection —
/// same "the superuser admin connection used for dev seeding" carve-out V001's own header comment
/// documents. <c>waba.business_accounts</c> has no EF entity anywhere in this codebase (WaAdmin
/// only ever reads it via a raw SQL scalar query — see WaAdminDbContext.GetBusinessAccountMetaWabaIdAsync),
/// so raw SQL is the only option for it regardless.
/// </summary>
public static class SqlSeeding
{
    public static async Task SeedTenantAsync(
        string adminConnectionString, Guid tenantId, string code, CancellationToken ct = default)
    {
        await using var connection = new NpgsqlConnection(adminConnectionString);
        await connection.OpenAsync(ct);
        await using var cmd = new NpgsqlCommand(
            """
            INSERT INTO tenancy.tenants (id, code, name, currency_code, country_code, timezone, status)
            VALUES (@id, @code, @name, 'INR', 'IN', 'Asia/Kolkata', 'active')
            """, connection);
        cmd.Parameters.AddWithValue("id", tenantId);
        cmd.Parameters.AddWithValue("code", code);
        cmd.Parameters.AddWithValue("name", $"Test tenant {code}");
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public static async Task SeedBusinessAccountAsync(
        string adminConnectionString, Guid businessAccountId, Guid tenantId, string metaWabaId, CancellationToken ct = default)
    {
        await using var connection = new NpgsqlConnection(adminConnectionString);
        await connection.OpenAsync(ct);
        await using var cmd = new NpgsqlCommand(
            """
            INSERT INTO waba.business_accounts (id, tenant_id, meta_waba_id, name)
            VALUES (@id, @tenantId, @metaWabaId, @name)
            """, connection);
        cmd.Parameters.AddWithValue("id", businessAccountId);
        cmd.Parameters.AddWithValue("tenantId", tenantId);
        cmd.Parameters.AddWithValue("metaWabaId", metaWabaId);
        cmd.Parameters.AddWithValue("name", $"Test WABA {metaWabaId}");
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public static async Task SeedPhoneNumberAsync(
        string adminConnectionString, Guid phoneNumberId, Guid tenantId, Guid businessAccountId,
        string metaPhoneNumberId, string displayPhoneNumber = "+15550000000", CancellationToken ct = default)
    {
        await using var connection = new NpgsqlConnection(adminConnectionString);
        await connection.OpenAsync(ct);
        await using var cmd = new NpgsqlCommand(
            """
            INSERT INTO waba.phone_numbers (id, tenant_id, business_account_id, meta_phone_number_id, display_phone_number, status)
            VALUES (@id, @tenantId, @businessAccountId, @metaPhoneNumberId, @displayPhoneNumber, 'CONNECTED')
            """, connection);
        cmd.Parameters.AddWithValue("id", phoneNumberId);
        cmd.Parameters.AddWithValue("tenantId", tenantId);
        cmd.Parameters.AddWithValue("businessAccountId", businessAccountId);
        cmd.Parameters.AddWithValue("metaPhoneNumberId", metaPhoneNumberId);
        cmd.Parameters.AddWithValue("displayPhoneNumber", displayPhoneNumber);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    /// <summary>Inserts a matched outbound_messages + outbound_outbox row pair, staged exactly as
    /// the real accept-path leaves them (status 'accepted' / 'pending') — same shape as PR #45's
    /// own live crash-reclaim reproduction (see
    /// .claude/agent-memory/qa-test-engineer/review-pr45-gateway-send.md). <c>next_attempt_at</c>
    /// is stamped from THIS process's own clock (<see cref="DateTimeOffset.UtcNow"/>), not the
    /// container's SQL <c>now()</c> — Testcontainers' Postgres runs inside the Docker daemon's own
    /// VM (Colima on this dev machine), whose clock is not guaranteed to be perfectly in sync with
    /// the test process's host clock; LeaseNextBatchAsync's own due-check compares against this
    /// same process's <c>DateTimeOffset.UtcNow</c>, so seeding from that clock (a few seconds in
    /// the past) is what makes "already due" deterministic regardless of any skew.</summary>
    public static async Task<Guid> SeedAcceptedMessageWithOutboxEntryAsync(
        string adminConnectionString, Guid tenantId, Guid phoneNumberId, CancellationToken ct = default)
    {
        var messageId = Guid.NewGuid();
        var outboxId = Guid.NewGuid();
        var dueAt = DateTimeOffset.UtcNow.AddSeconds(-10);

        await using var connection = new NpgsqlConnection(adminConnectionString);
        await connection.OpenAsync(ct);

        await using (var cmd = new NpgsqlCommand(
            """
            INSERT INTO messaging.outbound_messages
                (id, tenant_id, phone_number_id, to_wa_id, message_type, payload, idempotency_key, status, accepted_at)
            VALUES
                (@id, @tenantId, @phoneNumberId, '15550001234', 'text', '{"body":"hello"}', @idempotencyKey, 'accepted', @acceptedAt)
            """, connection))
        {
            cmd.Parameters.AddWithValue("id", messageId);
            cmd.Parameters.AddWithValue("tenantId", tenantId);
            cmd.Parameters.AddWithValue("phoneNumberId", phoneNumberId);
            cmd.Parameters.AddWithValue("idempotencyKey", $"itest-{messageId:N}");
            cmd.Parameters.AddWithValue("acceptedAt", dueAt);
            await cmd.ExecuteNonQueryAsync(ct);
        }

        await using (var cmd = new NpgsqlCommand(
            """
            INSERT INTO messaging.outbound_outbox
                (id, tenant_id, outbound_message_id, phone_number_id, status, next_attempt_at)
            VALUES
                (@id, @tenantId, @messageId, @phoneNumberId, 'pending', @nextAttemptAt)
            """, connection))
        {
            cmd.Parameters.AddWithValue("id", outboxId);
            cmd.Parameters.AddWithValue("tenantId", tenantId);
            cmd.Parameters.AddWithValue("messageId", messageId);
            cmd.Parameters.AddWithValue("phoneNumberId", phoneNumberId);
            cmd.Parameters.AddWithValue("nextAttemptAt", dueAt);
            await cmd.ExecuteNonQueryAsync(ct);
        }

        return outboxId;
    }

    /// <summary>Backdates outbound_outbox.locked_at past a given stale-lock window — simulates
    /// "instance A's lease has gone stale" without waiting for real time to pass, so a second
    /// dispatcher instance's own LeaseNextBatchAsync reclaim condition
    /// (<c>locked_at &lt; staleCutoff</c>) fires deterministically.</summary>
    public static async Task BackdateOutboxLockAsync(
        string adminConnectionString, Guid outboxEntryId, TimeSpan olderThan, CancellationToken ct = default)
    {
        await using var connection = new NpgsqlConnection(adminConnectionString);
        await connection.OpenAsync(ct);
        await using var cmd = new NpgsqlCommand(
            "UPDATE messaging.outbound_outbox SET locked_at = now() - @age WHERE id = @id", connection);
        cmd.Parameters.AddWithValue("age", olderThan);
        cmd.Parameters.AddWithValue("id", outboxEntryId);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public static async Task<(string Status, string? LockedBy, string? Wamid, string MessageStatus)> ReadOutboxAndMessageStateAsync(
        string adminConnectionString, Guid outboxEntryId, CancellationToken ct = default)
    {
        await using var connection = new NpgsqlConnection(adminConnectionString);
        await connection.OpenAsync(ct);
        await using var cmd = new NpgsqlCommand(
            """
            SELECT o.status, o.locked_by, m.wamid, m.status
            FROM messaging.outbound_outbox o
            JOIN messaging.outbound_messages m ON m.id = o.outbound_message_id
            WHERE o.id = @id
            """, connection);
        cmd.Parameters.AddWithValue("id", outboxEntryId);
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        await reader.ReadAsync(ct);
        return (
            reader.GetString(0),
            reader.IsDBNull(1) ? null : reader.GetString(1),
            reader.IsDBNull(2) ? null : reader.GetString(2),
            reader.GetString(3));
    }

    /// <summary>Pre-seeds a billing.usage_counters row so RecordMessageCostIdempotencyTests' TWO
    /// concurrent handler calls both hit an UPDATE on this already-existing row (no race there)
    /// instead of both racing to INSERT a brand-new one — which would trip
    /// <c>usage_counters_tenant_id_category_period_start_key</c> and mask the ONE constraint this
    /// test exists to exercise (<c>message_costs_wamid_key</c>). Matches
    /// <c>BillingPeriods.PeriodStart("monthly", DateTimeOffset.UtcNow)</c> exactly, since that is
    /// the key the real handler looks up by.</summary>
    public static async Task SeedUsageCounterAsync(
        string adminConnectionString, Guid tenantId, string category, CancellationToken ct = default)
    {
        var periodStart = new DateOnly(DateTime.UtcNow.Year, DateTime.UtcNow.Month, 1);

        await using var connection = new NpgsqlConnection(adminConnectionString);
        await connection.OpenAsync(ct);
        await using var cmd = new NpgsqlCommand(
            """
            INSERT INTO billing.usage_counters (id, tenant_id, category, period, period_start, message_count, billable_amount, currency)
            VALUES (@id, @tenantId, @category, 'monthly', @periodStart, 0, 0, 'INR')
            """, connection);
        cmd.Parameters.AddWithValue("id", Guid.NewGuid());
        cmd.Parameters.AddWithValue("tenantId", tenantId);
        cmd.Parameters.AddWithValue("category", category);
        cmd.Parameters.AddWithValue("periodStart", periodStart);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    /// <summary>Admin-connection (RLS-bypassing) count of billing.message_costs by wamid.</summary>
    public static async Task<long> CountMessageCostsByWamidAsync(
        string adminConnectionString, string wamid, CancellationToken ct = default)
    {
        await using var connection = new NpgsqlConnection(adminConnectionString);
        await connection.OpenAsync(ct);
        await using var cmd = new NpgsqlCommand(
            "SELECT count(*) FROM billing.message_costs WHERE wamid = @wamid", connection);
        cmd.Parameters.AddWithValue("wamid", wamid);
        return (long)(await cmd.ExecuteScalarAsync(ct))!;
    }
}
