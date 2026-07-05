using WaBilling.Application.Common.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace WaBilling.Infrastructure.TenantResolution;

/// <summary>
/// Resolves a tenant by looking up Meta's raw <c>phone_number_id</c> in <c>waba.phone_numbers</c>
/// (issue #19 — identical rationale and query to WaIntel's <c>WabaPhoneNumberTenantResolver</c>,
/// issue #15). The status webhook is published (by wa-ingest-svc) with an UNRESOLVED tenant —
/// resolving one requires a privileged (superuser) connection, since there is no tenant GUC to
/// set going in and this table is itself RLS-scoped. This is the single narrowly-scoped read
/// this resolver performs.
///
/// Wave 1 reality (unchanged from issue #15): <c>waba.phone_numbers</c> is empty until WABA
/// onboarding (issue #6) ships, so every lookup here returns null today — expected, not a bug.
/// <see cref="ITenantResolver"/>'s contract requires callers to treat null as "park this event."
/// </summary>
public sealed partial class WabaPhoneNumberTenantResolver : ITenantResolver
{
    private readonly string _adminConnectionString;
    private readonly ILogger<WabaPhoneNumberTenantResolver> _logger;

    public WabaPhoneNumberTenantResolver(IConfiguration configuration, ILogger<WabaPhoneNumberTenantResolver> logger)
    {
        _adminConnectionString = configuration.GetConnectionString("Admin")
            ?? throw new InvalidOperationException("ConnectionStrings:Admin is not configured.");
        _logger = logger;
    }

    public async Task<ResolvedTenant?> ResolveAsync(string metaPhoneNumberId, CancellationToken cancellationToken)
    {
        await using var connection = new NpgsqlConnection(_adminConnectionString);
        await connection.OpenAsync(cancellationToken);

        await using var command = new NpgsqlCommand(
            "SELECT id, tenant_id FROM waba.phone_numbers WHERE meta_phone_number_id = @metaId LIMIT 1",
            connection);
        command.Parameters.AddWithValue("@metaId", metaPhoneNumberId);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            LogUnresolved(_logger, metaPhoneNumberId);
            return null;
        }

        var phoneNumberId = reader.GetGuid(0);
        var tenantId = reader.GetGuid(1);
        return new ResolvedTenant(tenantId, phoneNumberId);
    }

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "No waba.phone_numbers row for Meta phone_number_id {MetaPhoneNumberId} — parking event (Wave 1: onboarding hasn't provisioned this number yet)")]
    private static partial void LogUnresolved(ILogger logger, string metaPhoneNumberId);
}
