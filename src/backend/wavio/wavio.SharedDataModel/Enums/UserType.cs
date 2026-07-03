namespace wavio.SharedDataModel.Enums;

/// <summary>
/// The coarse account/role type used for auth scope resolution. Extend with whatever
/// account types the new project actually needs.
/// </summary>
public static class UserType
{
    public const string PlatformAdmin = "platform_admin";
    public const string TenantAdmin = "tenant_admin";
    public const string Staff = "staff";
    public const string Auditor = "auditor";
    public const string Support = "support";

    public static readonly IReadOnlySet<string> All = new HashSet<string>
    {
        PlatformAdmin, TenantAdmin, Staff, Auditor, Support,
    };

    public static bool IsValid(string? value) => value is not null && All.Contains(value);
}
