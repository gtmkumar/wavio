using wavio.SharedDataModel.Entities.Messaging;
using wavio.SharedDataModel.Entities.Quality;
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

    Task<int> SaveChangesAsync(CancellationToken cancellationToken);
}
