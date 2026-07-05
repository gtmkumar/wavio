using WaIntel.Application.Quality.Logic;
using Xunit;

namespace WaIntel.Tests.Quality.Logic;

public class GuardianRulesTests
{
    [Fact]
    public void DetermineIncidentType_Yellow_ReturnsQualityYellow()
    {
        Assert.Equal(GuardianRules.IncidentQualityYellow, GuardianRules.DetermineIncidentType(QualityCodes.Yellow));
    }

    [Fact]
    public void DetermineIncidentType_Red_ReturnsQualityRed()
    {
        Assert.Equal(GuardianRules.IncidentQualityRed, GuardianRules.DetermineIncidentType(QualityCodes.Red));
    }

    [Theory]
    [InlineData(QualityCodes.Green)]
    [InlineData(QualityCodes.Unknown)]
    public void DetermineIncidentType_GreenOrUnknown_ReturnsNull(string rating)
    {
        Assert.Null(GuardianRules.DetermineIncidentType(rating));
    }

    [Fact]
    public void DetermineThrottleAction_QualityYellow_Is50Pct()
    {
        Assert.Equal(GuardianRules.Throttle50Pct, GuardianRules.DetermineThrottleAction(GuardianRules.IncidentQualityYellow));
    }

    [Fact]
    public void DetermineThrottleAction_QualityRed_IsFrozen()
    {
        Assert.Equal(GuardianRules.ThrottleFrozen, GuardianRules.DetermineThrottleAction(GuardianRules.IncidentQualityRed));
    }

    [Fact]
    public void DetermineThrottleAction_TierDowngrade_IsNone()
    {
        // Tier downgrades are informational only in v1 (spec §4.6) — no send throttling.
        Assert.Equal(GuardianRules.ThrottleNone, GuardianRules.DetermineThrottleAction(GuardianRules.IncidentTierDowngrade));
    }

    [Fact]
    public void DetermineSeverity_QualityRed_IsCritical()
    {
        Assert.Equal("critical", GuardianRules.DetermineSeverity(GuardianRules.IncidentQualityRed));
    }

    [Fact]
    public void DetermineSeverity_QualityYellow_IsWarning()
    {
        Assert.Equal("warning", GuardianRules.DetermineSeverity(GuardianRules.IncidentQualityYellow));
    }

    [Fact]
    public void DetermineSeverity_TierDowngrade_IsInfo()
    {
        Assert.Equal("info", GuardianRules.DetermineSeverity(GuardianRules.IncidentTierDowngrade));
    }

    [Fact]
    public void ShouldResolveOnRecovery_Green_IsTrue()
    {
        Assert.True(GuardianRules.ShouldResolveOnRecovery(QualityCodes.Green));
    }

    [Theory]
    [InlineData(QualityCodes.Yellow)]
    [InlineData(QualityCodes.Red)]
    [InlineData(QualityCodes.Unknown)]
    public void ShouldResolveOnRecovery_NotGreen_IsFalse(string rating)
    {
        Assert.False(GuardianRules.ShouldResolveOnRecovery(rating));
    }
}
