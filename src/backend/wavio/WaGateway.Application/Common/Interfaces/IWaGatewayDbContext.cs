using wavio.SharedDataModel.Entities.Messaging;
using wavio.SharedDataModel.Entities.Waba;
using Microsoft.EntityFrameworkCore;

namespace WaGateway.Application.Common.Interfaces;

/// <summary>
/// The wa-gateway-svc data-access surface (issue #14), exposed to Application handlers as an
/// interface — same convention as core.Application's <c>ICoreDbContext</c> / WaIntel's
/// <c>IWaIntelDbContext</c>. Backed by the shared <c>WavioDbContext</c> via an adapter in
/// WaGateway.Infrastructure. Tenant-scoped (RLS) for <see cref="OutboundMessages"/> and
/// <see cref="WabaPhoneNumbers"/>; NOT RLS-scoped for <see cref="OutboundOutboxEntries"/>
/// (db/migrations/V007__messaging.sql — the dispatcher drains every tenant's queue with no
/// tenant context, but still needs a tenant-scoped lookup against <c>waba.phone_numbers</c> and
/// <c>outbound_messages</c> per entry — see <c>ScopedCurrentTenant</c> for how that's done).
/// </summary>
public interface IWaGatewayDbContext
{
    DbSet<OutboundMessage> OutboundMessages { get; }
    DbSet<OutboundOutboxEntry> OutboundOutboxEntries { get; }
    DbSet<WabaPhoneNumber> WabaPhoneNumbers { get; }

    Task<int> SaveChangesAsync(CancellationToken cancellationToken);
}
