using WaBilling.Application.Quotas.Commands.CheckQuota;
using wavio.SharedDataModel.Entities.Billing;
using Xunit;

namespace WaBilling.Tests.Quotas;

public class CheckQuotaCommandHandlerTests
{
    private static readonly Guid TenantId = Guid.NewGuid();

    private static TenantQuota Quota(Guid tenantId, string category, long? soft, long? hard) => new()
    {
        Id = Guid.NewGuid(),
        TenantId = tenantId,
        Category = category,
        Period = "monthly",
        LimitUnit = "messages",
        SoftLimit = soft,
        HardLimit = hard,
        Enabled = true,
    };

    private static UsageCounter Counter(Guid tenantId, string category, long messageCount) => new()
    {
        Id = Guid.NewGuid(),
        TenantId = tenantId,
        Category = category,
        Period = "monthly",
        PeriodStart = new DateOnly(DateTime.UtcNow.Year, DateTime.UtcNow.Month, 1),
        MessageCount = messageCount,
        Currency = "INR",
    };

    [Fact]
    public async Task HandleAsync_NoQuotaConfigured_AllowsUnconditionally()
    {
        await using var db = InMemoryWaBillingDbContext.Create(nameof(HandleAsync_NoQuotaConfigured_AllowsUnconditionally));
        var handler = new CheckQuotaCommandHandler(db);

        var result = await handler.HandleAsync(new CheckQuotaCommand(TenantId, "marketing"), CancellationToken.None);

        Assert.True(result.Allowed);
        Assert.False(result.Blocked);
    }

    [Fact]
    public async Task HandleAsync_MarketingAtHardLimit_BlocksAndStampsHardLimitBlockedAt()
    {
        await using var db = InMemoryWaBillingDbContext.Create(nameof(HandleAsync_MarketingAtHardLimit_BlocksAndStampsHardLimitBlockedAt));
        db.TenantQuotas.Add(Quota(TenantId, "marketing", soft: 80, hard: 100));
        db.UsageCounters.Add(Counter(TenantId, "marketing", 100));
        await db.SaveChangesAsync(CancellationToken.None);

        var result = await new CheckQuotaCommandHandler(db)
            .HandleAsync(new CheckQuotaCommand(TenantId, "marketing"), CancellationToken.None);

        Assert.False(result.Allowed);
        Assert.True(result.Blocked);
        Assert.True(result.HardLimitReached);

        var counter = Assert.Single(db.UsageCounters);
        Assert.NotNull(counter.HardLimitBlockedAt);
    }

    [Theory]
    [InlineData("utility")]
    [InlineData("authentication")]
    [InlineData("service")]
    public async Task HandleAsync_NonMarketingAtHardLimit_NeverBlocksButReportsHardLimitReached(string category)
    {
        await using var db = InMemoryWaBillingDbContext.Create(
            nameof(HandleAsync_NonMarketingAtHardLimit_NeverBlocksButReportsHardLimitReached) + category);
        db.TenantQuotas.Add(Quota(TenantId, category, soft: 80, hard: 100));
        db.UsageCounters.Add(Counter(TenantId, category, 150)); // well past the hard limit
        await db.SaveChangesAsync(CancellationToken.None);

        var result = await new CheckQuotaCommandHandler(db)
            .HandleAsync(new CheckQuotaCommand(TenantId, category), CancellationToken.None);

        Assert.True(result.Allowed);
        Assert.False(result.Blocked);
        Assert.True(result.HardLimitReached);

        // Never blocked, so no block stamp — even though the limit was exceeded.
        var counter = Assert.Single(db.UsageCounters);
        Assert.Null(counter.HardLimitBlockedAt);
    }

    [Fact]
    public async Task HandleAsync_SoftLimitReachedButNotHard_AllowsAndStampsSoftAlertOnce()
    {
        await using var db = InMemoryWaBillingDbContext.Create(nameof(HandleAsync_SoftLimitReachedButNotHard_AllowsAndStampsSoftAlertOnce));
        db.TenantQuotas.Add(Quota(TenantId, "marketing", soft: 80, hard: 100));
        db.UsageCounters.Add(Counter(TenantId, "marketing", 85));
        await db.SaveChangesAsync(CancellationToken.None);

        var handler = new CheckQuotaCommandHandler(db);
        var first = await handler.HandleAsync(new CheckQuotaCommand(TenantId, "marketing"), CancellationToken.None);

        Assert.True(first.Allowed);
        Assert.False(first.Blocked);
        Assert.True(first.SoftLimitReached);

        var counter = Assert.Single(db.UsageCounters);
        var firstAlertAt = counter.SoftLimitAlertedAt;
        Assert.NotNull(firstAlertAt);

        // A second check within the same period must not re-stamp (the timestamp is stable).
        await handler.HandleAsync(new CheckQuotaCommand(TenantId, "marketing"), CancellationToken.None);
        Assert.Equal(firstAlertAt, db.UsageCounters.Single().SoftLimitAlertedAt);
    }

    [Fact]
    public async Task HandleAsync_UsageWellBelowEitherLimit_AllowsWithNoStamps()
    {
        await using var db = InMemoryWaBillingDbContext.Create(nameof(HandleAsync_UsageWellBelowEitherLimit_AllowsWithNoStamps));
        db.TenantQuotas.Add(Quota(TenantId, "marketing", soft: 80, hard: 100));
        db.UsageCounters.Add(Counter(TenantId, "marketing", 10));
        await db.SaveChangesAsync(CancellationToken.None);

        var result = await new CheckQuotaCommandHandler(db)
            .HandleAsync(new CheckQuotaCommand(TenantId, "marketing"), CancellationToken.None);

        Assert.True(result.Allowed);
        Assert.False(result.SoftLimitReached);
        Assert.False(result.HardLimitReached);

        var counter = Assert.Single(db.UsageCounters);
        Assert.Null(counter.SoftLimitAlertedAt);
        Assert.Null(counter.HardLimitBlockedAt);
    }

    [Fact]
    public async Task HandleAsync_TenantWideAllCategoryQuotaHardBreached_BlocksAMarketingSend()
    {
        await using var db = InMemoryWaBillingDbContext.Create(nameof(HandleAsync_TenantWideAllCategoryQuotaHardBreached_BlocksAMarketingSend));
        db.TenantQuotas.Add(Quota(TenantId, "all", soft: 900, hard: 1000));
        db.UsageCounters.Add(Counter(TenantId, "all", 1000));
        await db.SaveChangesAsync(CancellationToken.None);

        var result = await new CheckQuotaCommandHandler(db)
            .HandleAsync(new CheckQuotaCommand(TenantId, "marketing"), CancellationToken.None);

        Assert.True(result.Blocked);
    }
}
