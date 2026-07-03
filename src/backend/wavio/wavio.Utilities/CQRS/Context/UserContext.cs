namespace Wavio.Utilities.CQRS.Context;

/// <summary>
/// Snapshot of the authenticated principal for the current request, consumed by the
/// audit behavior and any handler that needs the acting user/tenant.
/// </summary>
public sealed class UserContext
{
    public UserContext(
        string? userId = null,
        string? tenantId = null,
        IReadOnlyCollection<string>? roles = null)
    {
        UserId = userId;
        TenantId = tenantId;
        Roles = roles ?? Array.Empty<string>();
    }

    public string? UserId { get; }

    public string? TenantId { get; }

    public IReadOnlyCollection<string> Roles { get; }

    public bool IsAuthenticated => !string.IsNullOrEmpty(UserId);
}
