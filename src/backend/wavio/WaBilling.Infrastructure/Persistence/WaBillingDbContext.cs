using WaBilling.Application.Common.Interfaces;
using wavio.SharedDataModel.Entities.Billing;
using wavio.SharedDataModel.Entities.Waba;
using wavio.SharedDataModel.Persistence;
using Microsoft.EntityFrameworkCore;

namespace WaBilling.Infrastructure.Persistence;

/// <summary>
/// Adapts the shared <see cref="WavioDbContext"/> to <see cref="IWaBillingDbContext"/>, exposing
/// only the <c>billing</c> schema entity sets this service owns (plus a read-only view of
/// <c>waba.phone_numbers</c> for the estimator's tier lookup). Same pattern as WaIntel's
/// <c>WaIntelDbContext</c> / WaAdmin's <c>WaAdminDbContext</c>.
/// </summary>
public sealed class WaBillingDbContext : IWaBillingDbContext
{
    private readonly WavioDbContext _db;

    public WaBillingDbContext(WavioDbContext db) => _db = db;

    public DbSet<RateCard> RateCards => _db.RateCards;
    public DbSet<RateCardEntry> RateCardEntries => _db.RateCardEntries;
    public DbSet<MessageCost> MessageCosts => _db.MessageCosts;
    public DbSet<TenantQuota> TenantQuotas => _db.TenantQuotas;
    public DbSet<UsageCounter> UsageCounters => _db.UsageCounters;
    public DbSet<InvoiceFeed> InvoicesFeed => _db.InvoicesFeed;
    public DbSet<WabaPhoneNumber> WabaPhoneNumbers => _db.WabaPhoneNumbers;

    public Task<int> SaveChangesAsync(CancellationToken cancellationToken) =>
        _db.SaveChangesAsync(cancellationToken);

    /// <inheritdoc />
    public async Task SetTenantContextAsync(Guid tenantId, CancellationToken cancellationToken)
    {
        // Same reasoning as WaIntel's WaIntelDbContext.SetTenantContextAsync: opening the
        // connection explicitly here means RlsConnectionInterceptor's connection-open GUC set
        // (which would otherwise write an EMPTY app.tenant_id — there's no HttpContext in the
        // RabbitMQ consumer) only fires once, before this override, and this value survives every
        // subsequent command on the same connection for the rest of this unit of work's lifetime.
        await _db.Database.OpenConnectionAsync(cancellationToken);

        var tenantIdText = tenantId.ToString();
        await _db.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT set_config('app.tenant_id', {tenantIdText}, false)", cancellationToken);
    }
}
