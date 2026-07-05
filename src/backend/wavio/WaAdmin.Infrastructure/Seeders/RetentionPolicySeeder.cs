using Microsoft.Extensions.Logging;
using Npgsql;

namespace WaAdmin.Infrastructure.Seeders;

/// <summary>
/// Seeds the platform-DEFAULT (NULL-tenant) <c>consent.retention_policies</c> rows (issue #21,
/// spec §4.10) — these are baseline reference data every tenant reads via the nullable-tenant RLS
/// pattern, NOT sensitive bootstrap credentials, so unlike <c>core.Infrastructure.Seeders
/// .IdentitySeeder</c> this deliberately runs in EVERY environment (not Development-only) and is
/// called unconditionally from WaAdmin.WebApi's Program.cs whenever a connection string is
/// configured. Idempotent via <c>ON CONFLICT (tenant_id, data_class) DO NOTHING</c> against the
/// migration's own NULLS-NOT-DISTINCT unique index (V012) — safe to run on every boot.
///
/// Runs on the privileged Admin connection via raw ADO.NET (same convention as
/// WaIntel.Infrastructure's <c>HealthSnapshotRollupService</c>/<c>WabaPhoneNumberTenantResolver</c>
/// for privileged cross-tenant writes), NOT the shared <c>WavioDbContext</c>/EF: inserting a
/// NULL-tenant row under RLS's normal app_user connection is possible in principle (the policy's
/// WITH CHECK allows "tenant_id IS NULL" unconditionally), but going through the same privileged
/// path as every other cross-tenant bootstrap write avoids relying on that OR-clause evaluation
/// order and keeps this seeder trivially simple (five parameterized INSERTs, no DbContext wiring).
///
/// Retention-day values are the spec's own numbers (§4.10: "message content 12 months,
/// metadata/cost ledger 8 years for tax") plus two judgment calls, documented here since the spec
/// doesn't give exact numbers for them: consent_evidence at 8 years (same tax/audit-record bucket
/// as metadata/cost_ledger — DPDP consent evidence is exactly the kind of record a regulator or
/// tax auditor could ask to see), and raw_webhook at 30 days, taken from spec §6's own schema
/// outline ("ingest.raw_webhooks (30-day TTL)") rather than invented independently.
/// </summary>
public sealed partial class RetentionPolicySeeder
{
    internal static readonly IReadOnlyList<(string DataClass, int RetentionDays, string Basis)> PlatformDefaults =
    [
        ("message_content", 365, "dpdp_default_12mo"),
        ("metadata", 2920, "tax_retention_8y"),
        ("cost_ledger", 2920, "tax_retention_8y"),
        ("consent_evidence", 2920, "tax_retention_8y"),
        ("raw_webhook", 30, "ingest_ttl"),
    ];

    private readonly string _adminConnectionString;
    private readonly ILogger<RetentionPolicySeeder> _logger;

    public RetentionPolicySeeder(string adminConnectionString, ILogger<RetentionPolicySeeder> logger)
    {
        _adminConnectionString = adminConnectionString;
        _logger = logger;
    }

    public async Task SeedAsync(CancellationToken cancellationToken)
    {
        await using var connection = new NpgsqlConnection(_adminConnectionString);
        await connection.OpenAsync(cancellationToken);

        var inserted = 0;
        foreach (var (dataClass, retentionDays, basis) in PlatformDefaults)
        {
            await using var command = new NpgsqlCommand(
                """
                INSERT INTO consent.retention_policies
                    (id, tenant_id, data_class, retention_days, basis, enabled, created_at, updated_at)
                VALUES
                    (gen_random_uuid(), NULL, @dataClass, @retentionDays, @basis, TRUE, now(), now())
                ON CONFLICT (tenant_id, data_class) DO NOTHING
                """, connection);
            command.Parameters.AddWithValue("@dataClass", dataClass);
            command.Parameters.AddWithValue("@retentionDays", retentionDays);
            command.Parameters.AddWithValue("@basis", basis);
            inserted += await command.ExecuteNonQueryAsync(cancellationToken);
        }

        LogSeeded(_logger, inserted, PlatformDefaults.Count);
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "RetentionPolicySeeder: inserted {Inserted} of {Total} platform-default retention policy row(s) (existing rows left untouched)")]
    private static partial void LogSeeded(ILogger logger, int inserted, int total);
}
