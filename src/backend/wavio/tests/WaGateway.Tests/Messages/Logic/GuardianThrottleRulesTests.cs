using WaGateway.Application.Messages.Logic;
using Xunit;

namespace WaGateway.Tests.Messages.Logic;

public class GuardianThrottleRulesTests
{
    [Fact]
    public void AppliesTo_Marketing_IsTrue()
    {
        Assert.True(GuardianThrottleRules.AppliesTo("marketing"));
    }

    [Theory]
    [InlineData("utility")]
    [InlineData("authentication")]
    [InlineData("service")]
    public void AppliesTo_NonMarketingCategories_IsFalse(string category)
    {
        // Never-block rule (spec §4.6): Guardian throttling never applies outside marketing.
        Assert.False(GuardianThrottleRules.AppliesTo(category));
    }

    [Fact]
    public void IsFrozen_MarketingFrozen_IsTrue()
    {
        Assert.True(GuardianThrottleRules.IsFrozen("marketing_frozen"));
    }

    [Theory]
    [InlineData("none")]
    [InlineData("marketing_50pct")]
    [InlineData(null)]
    public void IsFrozen_AnythingElse_IsFalse(string? throttleAction)
    {
        Assert.False(GuardianThrottleRules.IsFrozen(throttleAction));
    }

    [Fact]
    public void IsHalved_Marketing50Pct_IsTrue()
    {
        Assert.True(GuardianThrottleRules.IsHalved("marketing_50pct"));
    }

    [Theory]
    [InlineData("none")]
    [InlineData("marketing_frozen")]
    [InlineData(null)]
    public void IsHalved_AnythingElse_IsFalse(string? throttleAction)
    {
        Assert.False(GuardianThrottleRules.IsHalved(throttleAction));
    }
}
