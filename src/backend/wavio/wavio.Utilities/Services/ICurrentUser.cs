using wavio.Utilities.Auth;

namespace wavio.Utilities.Services;

/// <summary>UUID-based current-user context resolved from the request principal.
/// Cross-cutting: lives in Utilities so every bounded context can consume it.</summary>
public interface ICurrentUser
{
    Guid? UserId { get; }
    string? UserType { get; }
    string? Email { get; }
    string? Phone { get; }
    Guid? TenantId { get; }
    string? ScopeType { get; }
    Guid? ScopeId { get; }
    bool IsAuthenticated { get; }
    bool IsPlatformAdmin { get; }
    bool HasPermission(string permissionCode);

    /// <summary>Every scope-hierarchy node this user holds an active membership at
    /// (from the JWT <c>scope_nodes</c> claim). Empty for tokens issued before this claim
    /// existed or for non-staff principals. See <see cref="IsWithinScope"/>.</summary>
    IReadOnlyCollection<ScopeNode> ScopeNodes { get; }

    /// <summary>Ancestor-or-self boundary check for a target resource identified by its tenant.
    /// Returns true iff the user holds platform scope, OR a tenant membership matching
    /// <paramref name="tenantId"/>. Platform admins always pass. Coarse per-tenant RLS is a
    /// separate, complementary layer; this is the sub-tenant boundary RLS cannot express.
    /// Call it in a mutating handler AFTER loading the resource, passing the resource's tenant id.</summary>
    bool IsWithinScope(Guid? tenantId = null);

    /// <summary>Effective tenant without throwing: the X-Tenant-Id override (HttpContext
    /// item "tenant_id_override") if present, else JWT tenant_id, else null. Use for read
    /// paths that should degrade gracefully when no tenant context is set rather than 401.</summary>
    Guid? TryGetTenantId();

    /// <summary>Effective tenant for write operations. Platform admins: X-Tenant-Id override
    /// (HttpContext item "tenant_id_override") if present, else JWT tenant_id.
    /// Throws <see cref="UnauthorizedAccessException"/> if no tenant can be resolved.</summary>
    Guid RequireTenantId();
}
