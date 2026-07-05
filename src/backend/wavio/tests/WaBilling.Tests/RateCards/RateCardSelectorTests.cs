using WaBilling.Application.RateCards.Logic;
using wavio.SharedDataModel.Entities.Billing;
using Xunit;

namespace WaBilling.Tests.RateCards;

public class RateCardSelectorTests
{
    private static RateCard Card(DateOnly effectiveFrom, DateOnly? effectiveTo = null) => new()
    {
        Id = Guid.NewGuid(),
        Currency = "INR",
        EffectiveFrom = effectiveFrom,
        EffectiveTo = effectiveTo,
    };

    [Fact]
    public void SelectActiveCard_SingleCardEffectiveInThePast_IsSelected()
    {
        var card = Card(new DateOnly(2026, 1, 1));
        var result = RateCardSelector.SelectActiveCard([card], new DateOnly(2026, 7, 5));
        Assert.Same(card, result);
    }

    [Fact]
    public void SelectActiveCard_FutureDatedCardNotYetEffective_IsIgnored()
    {
        var current = Card(new DateOnly(2026, 4, 1));
        var future = Card(new DateOnly(2026, 10, 1)); // loaded in advance, not active yet

        var result = RateCardSelector.SelectActiveCard([current, future], new DateOnly(2026, 7, 5));

        Assert.Same(current, result);
    }

    [Fact]
    public void SelectActiveCard_OnceNowReachesTheFutureDatedCardsEffectiveFrom_ItBecomesActive()
    {
        var current = Card(new DateOnly(2026, 4, 1));
        var future = Card(new DateOnly(2026, 10, 1));

        // Same two cards, only "now" changed — no code change needed for the rollover.
        var result = RateCardSelector.SelectActiveCard([current, future], new DateOnly(2026, 10, 1));

        Assert.Same(future, result);
    }

    [Fact]
    public void SelectActiveCard_PicksTheGreatestEffectiveFromThatIsStillLessThanOrEqualToNow()
    {
        var jan = Card(new DateOnly(2026, 1, 1));
        var apr = Card(new DateOnly(2026, 4, 1));
        var jul = Card(new DateOnly(2026, 7, 1));

        var result = RateCardSelector.SelectActiveCard([jan, apr, jul], new DateOnly(2026, 7, 5));

        Assert.Same(jul, result);
    }

    [Fact]
    public void SelectActiveCard_CardPastItsEffectiveTo_IsExcluded()
    {
        var superseded = Card(new DateOnly(2026, 1, 1), new DateOnly(2026, 3, 31));
        var result = RateCardSelector.SelectActiveCard([superseded], new DateOnly(2026, 7, 5));
        Assert.Null(result);
    }

    [Fact]
    public void SelectActiveCard_NoCandidateCards_ReturnsNull()
    {
        var result = RateCardSelector.SelectActiveCard([], new DateOnly(2026, 7, 5));
        Assert.Null(result);
    }

    private static RateCardEntry Entry(string category, string market, string? tier, decimal price) => new()
    {
        Id = Guid.NewGuid(),
        Category = category,
        Market = market,
        VolumeTier = tier,
        PricePerMessage = price,
        Currency = "INR",
    };

    [Fact]
    public void SelectEntry_ExactTierMatch_PrefersTheTierSpecificRowOverTierAgnostic()
    {
        var entries = new[]
        {
            Entry("utility", "IN", null, 0.50m),
            Entry("utility", "IN", "TIER_10K", 0.30m),
        };

        var result = RateCardSelector.SelectEntry(entries, "utility", "IN", "TIER_10K");

        Assert.NotNull(result);
        Assert.Equal(0.30m, result!.PricePerMessage);
    }

    [Fact]
    public void SelectEntry_NoTierSpecificRowForRequestedTier_FallsBackToTierAgnosticRow()
    {
        var entries = new[]
        {
            Entry("utility", "IN", null, 0.50m),
            Entry("utility", "IN", "TIER_1K", 0.20m),
        };

        // Card only has a TIER_1K entry, but this phone number is TIER_10K — falls back to null.
        var result = RateCardSelector.SelectEntry(entries, "utility", "IN", "TIER_10K");

        Assert.NotNull(result);
        Assert.Equal(0.50m, result!.PricePerMessage);
    }

    [Fact]
    public void SelectEntry_MarketingNeverPassesATier_AlwaysUsesTheTierAgnosticRow()
    {
        var entries = new[] { Entry("marketing", "IN", null, 0.80m) };
        var result = RateCardSelector.SelectEntry(entries, "marketing", "IN", volumeTier: null);
        Assert.NotNull(result);
        Assert.Equal(0.80m, result!.PricePerMessage);
    }

    [Fact]
    public void SelectEntry_NoEntryForTheRequestedCategoryOrMarket_ReturnsNull()
    {
        var entries = new[] { Entry("utility", "IN", null, 0.50m) };
        var result = RateCardSelector.SelectEntry(entries, "marketing", "US", null);
        Assert.Null(result);
    }
}
