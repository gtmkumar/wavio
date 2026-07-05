using wavio.SharedDataModel.Entities.Quality;
using wavio.SharedDataModel.Entities.Sessions;
using wavio.SharedDataModel.Entities.Waba;
using Microsoft.EntityFrameworkCore;

namespace WaIntel.Application.Common.Interfaces;

/// <summary>
/// The wa-intel-svc data-access surface, exposed to Application handlers as an interface (no
/// repositories — same convention as core.Application's <c>ICoreDbContext</c> /
/// WaIngest.Application's <c>IWaIngestDbContext</c>). Backed by the shared
/// <c>WavioDbContext</c> via an adapter in WaIntel.Infrastructure. Tenant-scoped (RLS) — every
/// call runs under whatever <c>ICurrentTenant</c> the request/consumer scope resolved.
///
/// <see cref="WabaPhoneNumbers"/> (issue #20) is exposed here the same way WaGateway's
/// <c>IWaGatewayDbContext</c> already exposes it — both bounded contexts read/update the same
/// shared <c>waba.phone_numbers</c> table for the columns they each own use-cases for
/// (WaGateway: dispatch; WaIntel/Guardian: current quality rating + messaging tier).
/// </summary>
public interface IWaIntelDbContext
{
    DbSet<ConversationWindow> ConversationWindows { get; }
    DbSet<WindowEvent> WindowEvents { get; }
    DbSet<WabaPhoneNumber> WabaPhoneNumbers { get; }

    // quality (issue #20: Quality Rating Guardian, spec §4.6)
    DbSet<NumberQualityEvent> NumberQualityEvents { get; }
    DbSet<MessagingTierEvent> MessagingTierEvents { get; }
    DbSet<GuardianIncident> GuardianIncidents { get; }
    DbSet<HealthSnapshot> HealthSnapshots { get; }

    Task<int> SaveChangesAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Issues <c>pg_notify(channel, payload)</c> on the same connection as the rest of this
    /// unit of work — the LISTEN/NOTIFY half of the fast-lookup cache invalidation (issue #15,
    /// "DocSlot RBAC resolver pattern"). Call AFTER <see cref="SaveChangesAsync"/> has committed
    /// the row change the notification describes.
    /// </summary>
    Task NotifyAsync(string channel, string payload, CancellationToken cancellationToken);

    /// <summary>
    /// Explicitly (re-)sets the <c>app.tenant_id</c> RLS GUC on this unit of work's connection.
    ///
    /// Needed ONLY outside an HTTP request (the RabbitMQ consumer, see
    /// <c>MessageReceivedConsumerService</c>): <c>RlsConnectionInterceptor</c> sets the GUC once,
    /// at connection-open, from <c>ICurrentTenant.TenantId</c> — which is backed by
    /// <c>HttpContextCurrentTenant</c> and is always null with no HttpContext, so a
    /// background-scoped write would otherwise fail RLS's WITH CHECK (discovered live during
    /// issue #15 verification, not caught by unit tests since the in-memory provider doesn't
    /// enforce RLS at all). Calling this AFTER tenant resolution and BEFORE the first
    /// <see cref="SaveChangesAsync"/> overrides the GUC for the rest of this connection's
    /// lifetime (Postgres session GUCs persist until changed again; nothing else in this
    /// pipeline resets it afterward).
    /// </summary>
    Task SetTenantContextAsync(Guid tenantId, CancellationToken cancellationToken);
}
