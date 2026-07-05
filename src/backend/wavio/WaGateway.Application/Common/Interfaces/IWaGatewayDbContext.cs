using wavio.SharedDataModel.Entities.Messaging;
using wavio.SharedDataModel.Entities.Quality;
using wavio.SharedDataModel.Entities.Templates;
using wavio.SharedDataModel.Entities.Waba;
using Microsoft.EntityFrameworkCore;

namespace WaGateway.Application.Common.Interfaces;

/// <summary>
/// The wa-gateway-svc data-access surface (issue #14), exposed to Application handlers as an
/// interface — same convention as core.Application's <c>ICoreDbContext</c> / WaIntel's
/// <c>IWaIntelDbContext</c>. Backed by the shared <c>WavioDbContext</c> via an adapter in
/// WaGateway.Infrastructure. Tenant-scoped (RLS) for <see cref="OutboundMessages"/>,
/// <see cref="WabaPhoneNumbers"/> and <see cref="GuardianIncidents"/>; NOT RLS-scoped for
/// <see cref="OutboundOutboxEntries"/> (db/migrations/V007__messaging.sql — the dispatcher drains
/// every tenant's queue with no tenant context, but still needs a tenant-scoped lookup against
/// <c>waba.phone_numbers</c> and <c>outbound_messages</c> per entry — see
/// <c>ScopedCurrentTenant</c> for how that's done).
///
/// <see cref="GuardianIncidents"/> (issue #20, spec §4.6) is read directly here rather than via a
/// pg_notify + in-memory cache (the pattern WaIntel's own <c>WindowCacheInvalidationListener</c>
/// uses for its hot HTTP read path) — the outbox dispatcher already opens a tenant-scoped DB scope
/// per entry for <see cref="WabaPhoneNumbers"/>/<see cref="OutboundMessages"/> lookups, so one more
/// indexed query on the same connection gives zero-lag correctness (stronger than "propagates
/// within seconds") at negligible extra cost, without standing up a second LISTEN client process
/// in this service. Flagged as a deliberate convention deviation, not an oversight.
/// </summary>
public interface IWaGatewayDbContext
{
    DbSet<OutboundMessage> OutboundMessages { get; }
    DbSet<OutboundOutboxEntry> OutboundOutboxEntries { get; }
    DbSet<WabaPhoneNumber> WabaPhoneNumbers { get; }
    DbSet<GuardianIncident> GuardianIncidents { get; }

    /// <summary>
    /// messaging.suppression_list (issue #21, spec §4.10) — read here at accept time
    /// (<c>SendMessageHandler</c>), same "synchronous pre-dispatch gate" placement as the
    /// window-closed check, NOT the async outbox dispatcher: a suppressed recipient should get an
    /// immediate rejection in the HTTP response, not an async dead-letter minutes later. A row
    /// here means "no marketing" specifically (see <c>SuppressionListEntry</c>'s doc comment) —
    /// utility/authentication/service sends are never blocked by it.
    /// </summary>
    DbSet<SuppressionListEntry> SuppressionListEntries { get; }

    /// <summary>messaging.campaigns / campaign_recipients (issue #22, spec §4.2/§7.1) — the
    /// broadcast-with-tier-aware-chunking engine. <see cref="Campaigns"/> is written by the
    /// Create/Launch/Cancel command handlers; <see cref="CampaignRecipients"/> additionally by
    /// <c>CampaignChunkerService</c> (claims 'pending' rows, dispatches through
    /// <see cref="wavio.SharedDataModel.Entities.Messaging.OutboundMessage"/>'s own accept path)
    /// and <c>CampaignStatusConsumerService</c> (delivered/read/failed rollup off
    /// <c>wa.message.status.v1</c>).</summary>
    DbSet<Campaign> Campaigns { get; }
    DbSet<CampaignRecipient> CampaignRecipients { get; }

    /// <summary>templates.templates / template_versions (issue #16, cross-schema read-only —
    /// same "same-DB, different-schema reads are the established convention for background
    /// scanners" precedent as WaIntel's <c>HealthSnapshotRollupService</c>, issue #20 decision #5).
    /// A campaign pins a <see cref="TemplateVersion"/>; its category/name/language and current
    /// pause/disable state live on the parent <see cref="Template"/> row. WaGateway never writes
    /// to either table.</summary>
    DbSet<Template> Templates { get; }
    DbSet<TemplateVersion> TemplateVersions { get; }

    Task<int> SaveChangesAsync(CancellationToken cancellationToken);
}
