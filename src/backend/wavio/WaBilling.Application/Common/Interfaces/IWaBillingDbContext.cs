using wavio.SharedDataModel.Entities.Billing;
using wavio.SharedDataModel.Entities.Waba;
using Microsoft.EntityFrameworkCore;

namespace WaBilling.Application.Common.Interfaces;

/// <summary>
/// The wa-billing-svc data-access surface, exposed to Application handlers as an interface (no
/// repositories — same convention as core.Application's <c>ICoreDbContext</c> / WaIntel's
/// <c>IWaIntelDbContext</c>). Backed by the shared <c>WavioDbContext</c> via an adapter in
/// WaBilling.Infrastructure.
///
/// <see cref="RateCards"/>/<see cref="RateCardEntries"/> are platform-global reference data (no
/// RLS); everything else here is tenant-scoped (RLS) — every call runs under whatever
/// <c>ICurrentTenant</c> the request/consumer scope resolved. <see cref="WabaPhoneNumbers"/> is
/// read-only here (tier lookup for the estimator; issue #14 owns writes to it).
/// </summary>
public interface IWaBillingDbContext
{
    DbSet<RateCard> RateCards { get; }
    DbSet<RateCardEntry> RateCardEntries { get; }
    DbSet<MessageCost> MessageCosts { get; }
    DbSet<TenantQuota> TenantQuotas { get; }
    DbSet<UsageCounter> UsageCounters { get; }
    DbSet<InvoiceFeed> InvoicesFeed { get; }
    DbSet<WabaPhoneNumber> WabaPhoneNumbers { get; }

    Task<int> SaveChangesAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Explicitly (re-)sets the <c>app.tenant_id</c> RLS GUC on this unit of work's connection.
    /// Needed ONLY outside an HTTP request (the RabbitMQ status consumer) — see
    /// WaIntel.Application's <c>IWaIntelDbContext.SetTenantContextAsync</c> for the full
    /// rationale (RlsConnectionInterceptor resets the GUC to null on every new connection-open
    /// outside a request; this must be called AFTER tenant resolution and BEFORE the first
    /// <see cref="SaveChangesAsync"/> to survive for the rest of the unit of work).
    /// </summary>
    Task SetTenantContextAsync(Guid tenantId, CancellationToken cancellationToken);
}
