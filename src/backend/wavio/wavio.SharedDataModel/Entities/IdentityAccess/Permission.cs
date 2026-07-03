namespace wavio.SharedDataModel.Entities.IdentityAccess;

/// <summary>System-wide permission definition (identity_access.permissions).
/// Has created_at, updated_at, created_by, updated_by, status. No version, no deleted_at.</summary>
public class Permission
{
    public Guid Id { get; set; }
    public string Code { get; set; } = null!;
    public string Module { get; set; } = null!;
    /// <summary>Canonical owning navigator module (identity_access.modules.key) for entitlement.
    /// Null = unowned (orphan) → entitlement never filters it out. See permission_canonical_module.sql.</summary>
    public string? ModuleKey { get; set; }
    public string Action { get; set; } = null!;
    public string Name { get; set; } = null!;
    public string? Description { get; set; }
    public bool IsSystem { get; set; }
    public bool RequiresScope { get; set; }
    public string RiskLevel { get; set; } = null!;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public Guid? CreatedBy { get; set; }
    public Guid? UpdatedBy { get; set; }
    public string Status { get; set; } = null!;

    // Navigation
    public ICollection<RolePermission> RolePermissions { get; set; } = [];
}
