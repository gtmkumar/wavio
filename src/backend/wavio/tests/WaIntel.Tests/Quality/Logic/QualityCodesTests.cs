using WaIntel.Application.Quality.Logic;
using Xunit;

namespace WaIntel.Tests.Quality.Logic;

public class QualityCodesTests
{
    [Theory]
    [InlineData("GREEN", QualityCodes.Green)]
    [InlineData("green", QualityCodes.Green)]
    [InlineData("YELLOW", QualityCodes.Yellow)]
    [InlineData("RED", QualityCodes.Red)]
    [InlineData("UNKNOWN", QualityCodes.Unknown)]
    [InlineData("GREEN_QUALITY", QualityCodes.Green)]
    public void NormalizeRating_RecognizedVariants_MapToCanonicalLowercase(string raw, string expected)
    {
        Assert.Equal(expected, QualityCodes.NormalizeRating(raw));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("SOMETHING_ELSE")]
    public void NormalizeRating_UnrecognizedOrMissing_DegradesToUnknown(string? raw)
    {
        Assert.Equal(QualityCodes.Unknown, QualityCodes.NormalizeRating(raw));
    }

    [Fact]
    public void ToPhoneNumberRatingColumn_ReturnsUppercase_MatchingV002Check()
    {
        Assert.Equal("YELLOW", QualityCodes.ToPhoneNumberRatingColumn(QualityCodes.Yellow));
    }

    [Theory]
    [InlineData("TIER_250", "tier_250")]
    [InlineData("tier_1k", "tier_1k")]
    [InlineData("TIER_10K", "tier_10k")]
    [InlineData("TIER_100K", "tier_100k")]
    [InlineData("TIER_UNLIMITED", "tier_unlimited")]
    public void TryNormalizeTier_RecognizedMetaCode_MapsToCanonicalForm(string raw, string expectedCanonical)
    {
        Assert.True(QualityCodes.TryNormalizeTier(raw, out var canonical));
        Assert.Equal(expectedCanonical, canonical);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("TIER_50")]
    public void TryNormalizeTier_UnrecognizedOrMissing_ReturnsFalse(string? raw)
    {
        Assert.False(QualityCodes.TryNormalizeTier(raw, out _));
    }
}
