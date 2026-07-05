using WaAdmin.Application.Common.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace WaAdmin.Infrastructure.TenantResolution;

/// <summary>
/// Resolves a tenant by looking up Meta's raw <c>phone_number_id</c> in <c>waba.phone_numbers</c>
/// (issue #15 pattern, copied verbatim for issue #21's STOP-keyword listener — same reasoning as
/// WaIntel's/WaBilling's own copies: this table is RLS-scoped and, more fundamentally, the tenant
/// isn't known yet, so the ONLY way to run this lookup is a privileged connection).
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
        Message = "No waba.phone_numbers row for Meta phone_number_id {MetaPhoneNumberId} — parking event")]
    private static partial void LogUnresolved(ILogger logger, string metaPhoneNumberId);
}
