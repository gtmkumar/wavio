using WaIntel.Application.Quality.Logic;
using Xunit;

namespace WaIntel.Tests.Quality.Logic;

public class TierRulesTests
{
    [Fact]
    public void IsDowngrade_LowerRankedNewTier_IsTrue()
    {
        Assert.True(TierRules.IsDowngrade(TierRules.Tier10k, TierRules.Tier1k));
    }

    [Fact]
    public void IsDowngrade_HigherRankedNewTier_IsFalse()
    {
        Assert.False(TierRules.IsDowngrade(TierRules.Tier1k, TierRules.Tier10k));
    }

    [Fact]
    public void IsDowngrade_SameTier_IsFalse()
    {
        Assert.False(TierRules.IsDowngrade(TierRules.Tier1k, TierRules.Tier1k));
    }

    [Fact]
    public void IsDowngrade_NoPriorTierOnRecord_IsFalse()
    {
        // Nothing to compare against — a first-ever tier report is never a downgrade.
        Assert.False(TierRules.IsDowngrade(null, TierRules.Tier250));
    }

    [Fact]
    public void DailyLimitFor_Tier250_Is250()
    {
        Assert.Equal(250, TierRules.DailyLimitFor(TierRules.Tier250));
    }

    [Fact]
    public void DailyLimitFor_TierUnlimited_IsNull()
    {
        Assert.Null(TierRules.DailyLimitFor(TierRules.TierUnlimited));
    }

    [Fact]
    public void HeadroomFor_BelowLimit_ReturnsRemaining()
    {
        Assert.Equal(750, TierRules.HeadroomFor(TierRules.Tier1k, 250));
    }

    [Fact]
    public void HeadroomFor_AtOrAboveLimit_ReturnsZeroNotNegative()
    {
        Assert.Equal(0, TierRules.HeadroomFor(TierRules.Tier1k, 5_000));
    }

    [Fact]
    public void HeadroomFor_UnlimitedTier_ReturnsNull()
    {
        Assert.Null(TierRules.HeadroomFor(TierRules.TierUnlimited, 1_000_000));
    }

    [Fact]
    public void ComputeSafeDailySendPlan_HighestTier_NoFurtherGrowth()
    {
        var plan = TierRules.ComputeSafeDailySendPlan(TierRules.TierUnlimited, 50_000, QualityCodes.Green);

        Assert.Null(plan.NextTier);
        Assert.False(plan.ReadyToGrow);
    }

    [Fact]
    public void ComputeSafeDailySendPlan_NotGreenQuality_NeverRecommendsGrowth()
    {
        var plan = TierRules.ComputeSafeDailySendPlan(TierRules.Tier1k, 900, QualityCodes.Yellow);

        Assert.False(plan.ReadyToGrow);
        Assert.Equal(TierRules.Tier10k, plan.NextTier);
    }

    [Fact]
    public void ComputeSafeDailySendPlan_SustainingAtLeast80PctOfLimitAtGreen_IsReadyToGrow()
    {
        // Tier1k limit is 1000/day; 900 is 90% — above the 80% threshold.
        var plan = TierRules.ComputeSafeDailySendPlan(TierRules.Tier1k, 900, QualityCodes.Green);

        Assert.True(plan.ReadyToGrow);
        Assert.True(plan.RecommendedDailyVolume > 900);
    }

    [Fact]
    public void ComputeSafeDailySendPlan_BelowGrowthThreshold_NotReadyToGrow()
    {
        // 200/1000 = 20%, well below the 80% threshold.
        var plan = TierRules.ComputeSafeDailySendPlan(TierRules.Tier1k, 200, QualityCodes.Green);

        Assert.False(plan.ReadyToGrow);
        Assert.Equal(200, plan.RecommendedDailyVolume);
    }

    [Fact]
    public void ComputeSafeDailySendPlan_RecommendedVolume_NeverExceedsNextTiersLimit()
    {
        // Tier250 -> Tier1k: sustaining 240/day (96% of 250) at green would recommend 1.2x = 288,
        // but the next tier (1k) caps growth planning at 1000.
        var plan = TierRules.ComputeSafeDailySendPlan(TierRules.Tier250, 240, QualityCodes.Green);

        Assert.True(plan.RecommendedDailyVolume <= 1_000);
    }
}
