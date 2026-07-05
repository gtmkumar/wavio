using WaBilling.Application.Common.Interfaces;
using wavio.SharedDataModel.Entities.Billing;
using wavio.SharedDataModel.Entities.Waba;
using Microsoft.EntityFrameworkCore;

namespace WaBilling.Tests;

/// <summary>
/// Minimal <see cref="IWaBillingDbContext"/> stand-in for unit tests (same reasoning as
/// WaIntel.Tests' <c>InMemoryWaIntelDbContext</c>: <c>DbSet&lt;T&gt;</c> cannot be hand-faked).
/// The in-memory provider doesn't enforce RLS or the real DB's UNIQUE constraints (no
/// <c>OnModelCreating</c> override here — those live only on the real, database-first
/// <c>WavioDbContext</c>), so idempotency assertions in tests exercise the HANDLER's own
/// check-then-insert logic, not a DB-level constraint.
/// </summary>
public sealed class InMemoryWaBillingDbContext : DbContext, IWaBillingDbContext
{
    public InMemoryWaBillingDbContext(DbContextOptions<InMemoryWaBillingDbContext> options) : base(options) { }

    public DbSet<RateCard> RateCards => Set<RateCard>();
    public DbSet<RateCardEntry> RateCardEntries => Set<RateCardEntry>();
    public DbSet<MessageCost> MessageCosts => Set<MessageCost>();
    public DbSet<TenantQuota> TenantQuotas => Set<TenantQuota>();
    public DbSet<UsageCounter> UsageCounters => Set<UsageCounter>();
    public DbSet<InvoiceFeed> InvoicesFeed => Set<InvoiceFeed>();
    public DbSet<WabaPhoneNumber> WabaPhoneNumbers => Set<WabaPhoneNumber>();

    // The in-memory provider doesn't enforce RLS at all, so there's no GUC to set — this exists
    // only to satisfy the interface (same as InMemoryWaIntelDbContext).
    public Task SetTenantContextAsync(Guid tenantId, CancellationToken cancellationToken) => Task.CompletedTask;

    public static InMemoryWaBillingDbContext Create(string databaseName) =>
        new(new DbContextOptionsBuilder<InMemoryWaBillingDbContext>()
            .UseInMemoryDatabase(databaseName)
            .Options);
}
