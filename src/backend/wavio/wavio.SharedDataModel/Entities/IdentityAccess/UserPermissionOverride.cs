namespace wavio.SharedDataModel.Entities.IdentityAccess;

/// <summary>Per-user allow/deny on a single permission (identity_access.user_permission_override),
/// layered on top of role grants. Deny always wins. Lets you give a user a broad role with a
/// precise exception (or one extra permission) without minting a new role.
///
/// An override may be SCOPED (docs/rbac.md §7): a null <see cref="ScopeType"/> applies globally
/// across every scope the user holds; a set <see cref="ScopeType"/>/<see cref="ScopeId"/> applies
/// only within that node's subtree. It may also be time-boxed via <see cref="ExpiresAt"/> (an
/// expired row is ignored at resolution — enabling "suspend a capability until X"), and carries a
/// <see cref="Reason"/> for the audit trail.</summary>
public class UserPermissionOverride
{
    /// <summary>Surrogate key. Natural uniqueness is (user_id, permission_id, scope_type, scope_id)
    /// — enforced by a unique index — so a user can hold one global plus several scoped overrides
    /// for the same permission.</summary>
    public Guid Id { get; set; }

    public Guid UserId { get; set; }
    public Guid PermissionId { get; set; }
    public string Effect { get; set; } = "allow";  // 'allow' | 'deny'

    /// <summary>Optional scope this override is confined to (null = applies everywhere the user acts).
    /// One of <see cref="wavio.SharedDataModel.Enums.ScopeType"/>.</summary>
    public string? ScopeType { get; set; }
    /// <summary>Node id for <see cref="ScopeType"/> (null for a platform-scoped or global override).</summary>
    public Guid? ScopeId { get; set; }

    /// <summary>Optional human reason (why this user was granted/suspended this capability).</summary>
    public string? Reason { get; set; }
    /// <summary>Optional expiry; a row past this instant is ignored at permission resolution.</summary>
    public DateTimeOffset? ExpiresAt { get; set; }

    public DateTimeOffset GrantedAt { get; set; }
    public Guid? GrantedBy { get; set; }
    public DateTimeOffset CreatedAt { get; set; }

    public Permission Permission { get; set; } = null!;
}
