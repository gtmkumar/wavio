namespace wavio.SharedDataModel.Enums;

/// <summary>Scope type values used in user_scope_memberships, system_settings, etc.
/// Two levels: "platform" (global, every tenant) and "tenant" (scoped to one Tenant row).
/// Add more levels here if the new project's tenancy model needs a deeper hierarchy.</summary>
public static class ScopeType
{
    public const string Platform = "platform";
    public const string Tenant = "tenant";
}
