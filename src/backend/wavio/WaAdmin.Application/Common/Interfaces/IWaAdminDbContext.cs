using wavio.SharedDataModel.Entities.Templates;
using Microsoft.EntityFrameworkCore;

namespace WaAdmin.Application.Common.Interfaces;

/// <summary>
/// The wa-admin-svc data-access surface, exposed to Application handlers as an interface (no
/// repositories — same convention as core.Application's <c>ICoreDbContext</c> / WaIngest's
/// <c>IWaIngestDbContext</c>). Backed by the shared <c>WavioDbContext</c> via an adapter in
/// WaAdmin.Infrastructure. Only the <c>templates</c> schema entity sets this service owns
/// (issue #16) are surfaced here.
/// </summary>
public interface IWaAdminDbContext
{
    DbSet<Template> Templates { get; }
    DbSet<TemplateVersion> TemplateVersions { get; }
    DbSet<TemplateStatusEvent> TemplateStatusEvents { get; }
    DbSet<TemplateCategoryChange> TemplateCategoryChanges { get; }
    DbSet<TemplateLintResult> TemplateLintResults { get; }

    /// <summary>
    /// Looks up the Meta-side WABA id (waba.business_accounts.meta_waba_id) for a platform
    /// business-account row. Raw scalar read rather than a DbSet/navigation: wa-admin-svc's
    /// templates schema has a DB foreign key to <c>waba.business_accounts</c> (V009), but no EF
    /// entity for that schema exists yet (WABA onboarding is issue #6/#14) — this is the minimal
    /// surface needed to address Meta's Graph API by the right id without pulling in the whole
    /// waba bounded context. Returns null when the business account row does not exist.
    /// </summary>
    Task<string?> GetBusinessAccountMetaWabaIdAsync(Guid businessAccountId, CancellationToken cancellationToken);

    Task<int> SaveChangesAsync(CancellationToken cancellationToken);
}
