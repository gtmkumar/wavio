using WaIntel.Application.Quality.Logic;
using Xunit;

namespace WaIntel.Tests.Quality.Logic;

public class HealthMetricsRulesTests
{
    [Fact]
    public void DeliveryRate_ComputesPercentageRoundedTo2dp()
    {
        Assert.Equal(66.67m, HealthMetricsRules.DeliveryRate(sent: 3, delivered: 2));
    }

    [Fact]
    public void DeliveryRate_NoMessagesSent_IsZeroNotDivideByZero()
    {
        Assert.Equal(0m, HealthMetricsRules.DeliveryRate(sent: 0, delivered: 0));
    }

    [Fact]
    public void ReadRate_ComputesFromDelivered()
    {
        Assert.Equal(50.00m, HealthMetricsRules.ReadRate(delivered: 4, read: 2));
    }

    [Fact]
    public void BlockProxyRate_ComputesFromSentAndFailed()
    {
        Assert.Equal(10.00m, HealthMetricsRules.BlockProxyRate(sent: 100, failed: 10));
    }

    [Fact]
    public void IsBlockRateSpike_AtOrAboveThreshold_IsTrue()
    {
        Assert.True(HealthMetricsRules.IsBlockRateSpike(HealthMetricsRules.BlockRateSpikeThresholdPercent));
        Assert.True(HealthMetricsRules.IsBlockRateSpike(20m));
    }

    [Fact]
    public void IsBlockRateSpike_BelowThreshold_IsFalse()
    {
        Assert.False(HealthMetricsRules.IsBlockRateSpike(5m));
    }
}
