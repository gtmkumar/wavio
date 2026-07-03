using wavio.SharedDataModel.Common;
using wavio.SharedDataModel.Entities.IdentityAccess;

namespace wavio.SharedDataModel.Entities.TenancyOrg;

/// <summary>
/// Example tenant (tenancy.tenants) — the row-level-security scoping unit. Rename/extend
/// this to match the new project's actual tenancy model (single-tenant, org-per-customer,
/// multi-brand, etc.) or delete it and adjust ICurrentTenant/RlsConnectionInterceptor if the
/// project doesn't need multi-tenancy at all.
/// </summary>
public class Tenant : IAuditableEntity, ISoftDeletable
{
    public Guid Id { get; set; }
    public string Code { get; set; } = null!;
    public string Name { get; set; } = null!;
    public string CurrencyCode { get; set; } = null!;
    public string CountryCode { get; set; } = null!;
    public string Timezone { get; set; } = null!;
    public string Status { get; set; } = null!;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public Guid? CreatedBy { get; set; }
    public Guid? UpdatedBy { get; set; }
    public int Version { get; set; }
    public DateTimeOffset? DeletedAt { get; set; }

    // Navigations
    public ICollection<Role> Roles { get; set; } = [];
}
