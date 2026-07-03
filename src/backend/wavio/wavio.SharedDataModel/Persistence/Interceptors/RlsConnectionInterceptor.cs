using wavio.SharedDataModel.Contracts;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Npgsql;
using System.Data.Common;

namespace wavio.SharedDataModel.Persistence.Interceptors;

/// <summary>
/// Sets PostgreSQL session-level config variables for Row-Level Security on every connection open.
/// The DB RLS policies read: app.tenant_id (spec §5 canonical GUC), app.current_user_id, and app.bypass_rls.
/// Empty string is used for null/unset values — RLS policies treat empty as "unset".
/// </summary>
public sealed class RlsConnectionInterceptor : DbConnectionInterceptor
{
    private readonly ICurrentTenant _currentTenant;

    public RlsConnectionInterceptor(ICurrentTenant currentTenant)
    {
        _currentTenant = currentTenant;
    }

    public override void ConnectionOpened(DbConnection connection, ConnectionEndEventData eventData)
    {
        SetRlsVariables(connection);
    }

    public override async Task ConnectionOpenedAsync(
        DbConnection connection,
        ConnectionEndEventData eventData,
        CancellationToken cancellationToken = default)
    {
        await SetRlsVariablesAsync(connection, cancellationToken).ConfigureAwait(false);
    }

    private void SetRlsVariables(DbConnection connection)
    {
        using var cmd = connection.CreateCommand();
        BuildSetConfigCommand(cmd);
        cmd.ExecuteNonQuery();
    }

    private async Task SetRlsVariablesAsync(DbConnection connection, CancellationToken ct)
    {
        await using var cmd = connection.CreateCommand();
        BuildSetConfigCommand(cmd);
        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    private void BuildSetConfigCommand(DbCommand cmd)
    {
        var tenantId = _currentTenant.TenantId?.ToString() ?? string.Empty;
        var userId = _currentTenant.UserId?.ToString() ?? string.Empty;
        var bypassRls = _currentTenant.BypassRls ? "true" : "false";

        // set_config(setting_name, value, is_local) — false = session-level
        cmd.CommandText = """
            SELECT
                set_config('app.tenant_id',       @tenant_id,  false),
                set_config('app.current_user_id', @user_id,    false),
                set_config('app.bypass_rls',      @bypass_rls, false)
            """;

        AddParameter(cmd, "@tenant_id",  tenantId);
        AddParameter(cmd, "@user_id",    userId);
        AddParameter(cmd, "@bypass_rls", bypassRls);
    }

    private static void AddParameter(DbCommand cmd, string name, string value)
    {
        var p = cmd.CreateParameter();
        p.ParameterName = name;
        p.Value = value;
        cmd.Parameters.Add(p);
    }
}
