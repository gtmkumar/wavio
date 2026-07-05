using WaBilling.Application.Quotas.Logic;
using wavio.SharedDataModel.Entities.Billing;
using Xunit;

namespace WaBilling.Tests.Quotas;

public class QuotaRulesTests
{
    private static TenantQuota Quota(string limitUnit, long? soft, long? hard) => new()
    {
        LimitUnit = limitUnit,
        SoftLimit = soft,
        HardLimit = hard,
    };

    private static UsageCounter Counter(long messageCount, decimal billableAmount) => new()
    {
        MessageCount = messageCount,
        BillableAmount = billableAmount,
    };

    [Fact]
    public void CurrentValue_MessagesUnit_ReturnsMessageCount()
    {
        var value = QuotaRules.CurrentValue("messages", Counter(42, 999m));
        Assert.Equal(42m, value);
    }

    [Fact]
    public void CurrentValue_AmountUnit_ReturnsBillableAmount()
    {
        var value = QuotaRules.CurrentValue("amount", Counter(42, 12.5m));
        Assert.Equal(12.5m, value);
    }

    [Fact]
    public void CurrentValue_NoCounterYet_IsZero()
    {
        var value = QuotaRules.CurrentValue("messages", counter: null);
        Assert.Equal(0m, value);
    }

    [Fact]
    public void IsSoftBreached_UsageAtOrAboveSoftLimit_IsTrue()
    {
        var quota = Quota("messages", soft: 100, hard: 200);
        Assert.True(QuotaRules.IsSoftBreached(quota, 100m));
        Assert.True(QuotaRules.IsSoftBreached(quota, 150m));
    }

    [Fact]
    public void IsSoftBreached_UsageBelowSoftLimit_IsFalse()
    {
        var quota = Quota("messages", soft: 100, hard: 200);
        Assert.False(QuotaRules.IsSoftBreached(quota, 99m));
    }

    [Fact]
    public void IsSoftBreached_NoSoftLimitConfigured_IsAlwaysFalse()
    {
        var quota = Quota("messages", soft: null, hard: 200);
        Assert.False(QuotaRules.IsSoftBreached(quota, 1_000_000m));
    }

    [Fact]
    public void IsHardBreached_UsageAtOrAboveHardLimit_IsTrue()
    {
        var quota = Quota("messages", soft: 100, hard: 200);
        Assert.True(QuotaRules.IsHardBreached(quota, 200m));
    }

    [Fact]
    public void IsHardBreached_NoHardLimitConfigured_IsAlwaysFalse()
    {
        var quota = Quota("messages", soft: 100, hard: null);
        Assert.False(QuotaRules.IsHardBreached(quota, 1_000_000m));
    }

    [Theory]
    [InlineData("utility")]
    [InlineData("authentication")]
    [InlineData("service")]
    public void ShouldBlock_NonMarketingCategoryHardBreached_NeverBlocks(string category)
    {
        // Hard platform rule (spec §4.7): utility/authentication/service are never blocked,
        // no matter how badly the hard limit is breached.
        Assert.False(QuotaRules.ShouldBlock(category, hardBreached: true));
    }

    [Fact]
    public void ShouldBlock_MarketingHardBreached_Blocks()
    {
        Assert.True(QuotaRules.ShouldBlock("marketing", hardBreached: true));
    }

    [Fact]
    public void ShouldBlock_MarketingNotHardBreached_DoesNotBlock()
    {
        Assert.False(QuotaRules.ShouldBlock("marketing", hardBreached: false));
    }
}
