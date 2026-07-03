namespace core.Application.Identity.AccessControl;

internal static class AccessHelpers
{
    public static string Tier(string roleScopeType) => roleScopeType;

    public static string Initials(string name)
    {
        var parts = name.Split([' ', '.', '@'], StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0) return "?";
        var first = parts[0][..1];
        var second = parts.Length > 1 ? parts[1][..1] : "";
        return (first + second).ToUpperInvariant();
    }
}
