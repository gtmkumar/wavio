using WaGateway.Application.Campaigns.Logic;
using WaGateway.Application.Messages.Logic;
using Xunit;

namespace WaGateway.Tests.Campaigns.Logic;

public class CampaignTierRulesTests
{
    [Theory]
    [InlineData("TIER_250", 250)]
    [InlineData("TIER_1K", 1_000)]
    [InlineData("TIER_10K", 10_000)]
    [InlineData("TIER_100K", 100_000)]
    [InlineData("tier_1k", 1_000)] // case-insensitive — Meta's raw code, not a strict enum match
    public void DailyLimitForRawTier_returns_the_known_ceiling(string rawTier, int expectedLimit)
    {
        Assert.Equal(expectedLimit, CampaignTierRules.DailyLimitForRawTier(rawTier));
    }

    [Fact]
    public void DailyLimitForRawTier_TIER_UNLIMITED_returns_null()
    {
        Assert.Null(CampaignTierRules.DailyLimitForRawTier("TIER_UNLIMITED"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("TIER_9999_NOT_A_REAL_CODE")]
    public void DailyLimitForRawTier_unknown_or_missing_tier_falls_back_to_the_most_conservative_known_tier(string? rawTier)
    {
        // Fail-closed: never guess unlimited/generous for a tier Meta hasn't reported or this
        // platform doesn't recognize yet.
        Assert.Equal(250, CampaignTierRules.DailyLimitForRawTier(rawTier));
    }

    [Fact]
    public void ComputeHeadroom_subtracts_consumed_from_the_daily_limit()
    {
        Assert.Equal(750, CampaignTierRules.ComputeHeadroom(1_000, 250));
    }

    [Fact]
    public void ComputeHeadroom_never_goes_negative()
    {
        Assert.Equal(0, CampaignTierRules.ComputeHeadroom(1_000, 5_000));
    }

    [Fact]
    public void ComputeHeadroom_unlimited_tier_returns_MaxValue()
    {
        Assert.Equal(int.MaxValue, CampaignTierRules.ComputeHeadroom(null, 5_000));
    }

    [Fact]
    public void ApplyGuardianThrottle_halves_on_marketing_50pct()
    {
        Assert.Equal(500, CampaignTierRules.ApplyGuardianThrottle(1_000, GuardianThrottleRules.Throttle50Pct));
    }

    [Fact]
    public void ApplyGuardianThrottle_leaves_headroom_untouched_when_not_throttled()
    {
        Assert.Equal(1_000, CampaignTierRules.ApplyGuardianThrottle(1_000, GuardianThrottleRules.ThrottleNone));
        Assert.Equal(1_000, CampaignTierRules.ApplyGuardianThrottle(1_000, null));
    }

    [Fact]
    public void ComputeChunkSize_frozen_returns_zero_regardless_of_headroom_or_pending_count()
    {
        Assert.Equal(0, CampaignTierRules.ComputeChunkSize(10_000, 0, GuardianThrottleRules.ThrottleFrozen, 5_000));
    }

    [Fact]
    public void ComputeChunkSize_never_exceeds_pending_count()
    {
        Assert.Equal(10, CampaignTierRules.ComputeChunkSize(1_000, 0, null, 10));
    }

    [Fact]
    public void ComputeChunkSize_halved_throttle_applies_after_headroom_and_before_the_pending_cap()
    {
        // Headroom = 1000 - 200 = 800; halved = 400; pending (10000) is not the binding constraint.
        Assert.Equal(400, CampaignTierRules.ComputeChunkSize(1_000, 200, GuardianThrottleRules.Throttle50Pct, 10_000));
    }

    [Fact]
    public void ComputeChunkSize_headroom_fully_exhausted_returns_zero()
    {
        Assert.Equal(0, CampaignTierRules.ComputeChunkSize(1_000, 1_000, null, 500));
    }

    [Fact]
    public void ComputeChunkSize_unlimited_tier_is_capped_only_by_pending_count()
    {
        Assert.Equal(50, CampaignTierRules.ComputeChunkSize(null, 999_999, null, 50));
    }
}
