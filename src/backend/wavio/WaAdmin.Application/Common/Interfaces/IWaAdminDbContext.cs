using wavio.SharedDataModel.Entities.Consent;
using wavio.SharedDataModel.Entities.Messaging;
using wavio.SharedDataModel.Entities.Templates;
using wavio.SharedDataModel.Entities.Waba;
using Microsoft.EntityFrameworkCore;

namespace WaAdmin.Application.Common.Interfaces;

/// <summary>
/// The wa-admin-svc data-access surface, exposed to Application handlers as an interface (no
/// repositories — same convention as core.Application's <c>ICoreDbContext</c> / WaIngest's
/// <c>IWaIngestDbContext</c>). Backed by the shared <c>WavioDbContext</c> via an adapter in
/// WaAdmin.Infrastructure. Surfaces the <c>templates</c> schema (issue #16) and, as of issue
/// #21, the <c>consent</c> schema entity sets plus <see cref="SuppressionListEntries"/>
/// (messaging.suppression_list, V007 — the STOP listener writes here; wa-gateway-svc has its own
/// adapter exposing the same shared table for the pre-dispatch read).
/// </summary>
public interface IWaAdminDbContext
{
    DbSet<Template> Templates { get; }
    DbSet<TemplateVersion> TemplateVersions { get; }
    DbSet<TemplateStatusEvent> TemplateStatusEvents { get; }
    DbSet<TemplateCategoryChange> TemplateCategoryChanges { get; }
    DbSet<TemplateLintResult> TemplateLintResults { get; }

    /// <summary>Read-only picker surface for the admin console (GET /v1/waba/phone-numbers);
    /// the shared <c>WabaPhoneNumber</c> entity already existed for the outbox dispatcher.
    /// The onboarding wizard (docs/ONBOARDING_WIZARD_PLAN.md) also writes it, alongside the
    /// three sets below.</summary>
    DbSet<WabaPhoneNumber> WabaPhoneNumbers { get; }

    DbSet<WabaBusinessAccount> WabaBusinessAccounts { get; }
    DbSet<WabaBusinessProfile> WabaBusinessProfiles { get; }
    DbSet<WabaPhoneNumberEvent> WabaPhoneNumberEvents { get; }

    DbSet<OptInEvent> OptInEvents { get; }
    DbSet<OptOutEvent> OptOutEvents { get; }
    DbSet<ErasureRequest> ErasureRequests { get; }
    DbSet<RetentionPolicy> RetentionPolicies { get; }
    DbSet<SuppressionListEntry> SuppressionListEntries { get; }

    /// <summary>
    /// Looks up the Meta-side WABA id (waba.business_accounts.meta_waba_id) for a platform
    /// business-account row. Kept as a scalar convenience for the template-submit path even
    /// though <see cref="WabaBusinessAccounts"/> now exists (onboarding wizard).
    /// Returns null when the business account row does not exist.
    /// </summary>
    Task<string?> GetBusinessAccountMetaWabaIdAsync(Guid businessAccountId, CancellationToken cancellationToken);

    Task<int> SaveChangesAsync(CancellationToken cancellationToken);
}
