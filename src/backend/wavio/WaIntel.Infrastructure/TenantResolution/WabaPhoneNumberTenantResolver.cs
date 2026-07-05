using WaIntel.Application.Common.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace WaIntel.Infrastructure.TenantResolution;

/// <summary>
/// Resolves a tenant by looking up Meta's raw <c>phone_number_id</c> in <c>waba.phone_numbers</c>
/// (issue #15). This table is RLS-scoped and, more fundamentally, we don't know the tenant yet —
/// that's the whole point of resolving it — so there is no tenant GUC to set going in. The ONLY
/// way to run this lookup at all is a privileged (superuser) connection, exactly as the
/// database-architect's migration-runner design already does for cross-cutting bootstrap work.
/// This is the single narrowly-scoped read this resolver performs; it never touches window data.
///
/// Wave 1 reality (see WaIngest's own handoff notes): <c>waba.phone_numbers</c> is EMPTY — WABA
/// onboarding (issue #6) doesn't exist yet. Every lookup here returns null today. That is
/// expected, not a bug: <see cref="ITenantResolver"/>'s contract requires callers to treat null as
/// "park this event," and the consumer does exactly that (see
/// <c>MessageReceivedConsumerService</c>). Once onboarding ships, rows appear and resolution starts
/// succeeding with no code change here.
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
