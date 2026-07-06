using System.Text.Json;
using WaAdmin.Infrastructure.Templates;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace WaAdmin.Infrastructure.Seeders;

/// <summary>
/// Seeds the platform pre-approved vertical template packs (issue #27, spec §4.4) into the
/// NULL-tenant rows of templates.template_packs — reference content every tenant can read via the
/// nullable-tenant RLS pattern (V009's own <c>tenant_isolation</c> policy comment on that table),
/// not sensitive bootstrap credentials. Runs in EVERY environment (not Development-only), same
/// convention and rationale as <see cref="RetentionPolicySeeder"/>. Idempotent via
/// ON CONFLICT (tenant_id, pack_key) DO NOTHING against the migration's own NULLS-NOT-DISTINCT
/// unique index — safe to run on every boot.
///
/// Runs on the privileged Admin connection via raw ADO.NET, same convention as
/// <see cref="RetentionPolicySeeder"/> and for the same reason: a NULL-tenant insert under the
/// ordinary app_user RLS connection is possible in principle (the policy's WITH CHECK allows
/// "tenant_id IS NULL" unconditionally) but relies on that OR-clause evaluation order — going
/// through the same privileged cross-tenant-write path as every other bootstrap seeder avoids
/// that and keeps this trivially simple.
/// </summary>
public sealed partial class TemplatePackSeeder
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly string _adminConnectionString;
    private readonly ILogger<TemplatePackSeeder> _logger;

    public TemplatePackSeeder(string adminConnectionString, ILogger<TemplatePackSeeder> logger)
    {
        _adminConnectionString = adminConnectionString;
        _logger = logger;
    }

    public async Task SeedAsync(CancellationToken cancellationToken)
    {
        await using var connection = new NpgsqlConnection(_adminConnectionString);
        await connection.OpenAsync(cancellationToken);

        var inserted = 0;
        foreach (var pack in VerticalTemplatePacks.All)
        {
            // definitions is a jsonb ARRAY (templates.template_packs.definitions default '[]') —
            // one pack can in principle bundle multiple definition variants; v1 seeds exactly one
            // per pack.
            var definitionsJson = JsonSerializer.Serialize(new[] { pack.Definition }, JsonOptions);

            await using var command = new NpgsqlCommand(
                """
                INSERT INTO templates.template_packs
                    (id, tenant_id, pack_key, vertical, name, description, definitions, status, created_at, updated_at, version)
                VALUES
                    (gen_random_uuid(), NULL, @packKey, @vertical, @name, @description, @definitions::jsonb, 'active', now(), now(), 1)
                ON CONFLICT (tenant_id, pack_key) DO NOTHING
                """, connection);
            command.Parameters.AddWithValue("@packKey", pack.PackKey);
            command.Parameters.AddWithValue("@vertical", pack.Vertical);
            command.Parameters.AddWithValue("@name", pack.Name);
            command.Parameters.AddWithValue("@description", pack.Description);
            command.Parameters.AddWithValue("@definitions", definitionsJson);
            inserted += await command.ExecuteNonQueryAsync(cancellationToken);
        }

        LogSeeded(_logger, inserted, VerticalTemplatePacks.All.Count);
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "TemplatePackSeeder: inserted {Inserted} of {Total} platform vertical template pack row(s) (existing rows left untouched)")]
    private static partial void LogSeeded(ILogger logger, int inserted, int total);
}
