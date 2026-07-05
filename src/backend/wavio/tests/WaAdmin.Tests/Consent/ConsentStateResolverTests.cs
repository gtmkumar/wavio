using WaAdmin.Application.Consent.Logic;
using Xunit;

namespace WaAdmin.Tests.Consent;

public class ConsentStateResolverTests
{
    private static readonly DateTimeOffset Day1 = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Day2 = Day1.AddDays(1);
    private static readonly DateTimeOffset Day3 = Day1.AddDays(2);

    [Fact]
    public void Resolve_NoOptInEverRecorded_PurposeIsNotOptedIn()
    {
        var result = ConsentStateResolver.Resolve([], []);

        Assert.All(result, p => Assert.False(p.OptedIn));
    }

    [Fact]
    public void Resolve_OptInWithNoOptOut_PurposeIsOptedIn()
    {
        var result = ConsentStateResolver.Resolve([("marketing", Day1)], []);

        var marketing = result.Single(p => p.Purpose == "marketing");
        Assert.True(marketing.OptedIn);
        Assert.Equal(Day1, marketing.LastOptInAt);
    }

    [Fact]
    public void Resolve_OptOutAfterOptIn_SamePurposeScope_PurposeIsNotOptedIn()
    {
        var result = ConsentStateResolver.Resolve(
            [("marketing", Day1)], [("marketing", Day2)]);

        var marketing = result.Single(p => p.Purpose == "marketing");
        Assert.False(marketing.OptedIn);
    }

    [Fact]
    public void Resolve_OptInAfterOptOut_PurposeIsOptedInAgain()
    {
        // A later re-opt-in overrides an earlier opt-out — most-recent-event wins.
        var result = ConsentStateResolver.Resolve(
            [("marketing", Day3)], [("marketing", Day2)]);

        var marketing = result.Single(p => p.Purpose == "marketing");
        Assert.True(marketing.OptedIn);
    }

    [Fact]
    public void Resolve_MarketingScopeOptOut_DoesNotAffectTransactionalOrServicePurpose()
    {
        var result = ConsentStateResolver.Resolve(
            [("transactional", Day1), ("marketing", Day1), ("service", Day1)],
            [("marketing", Day2)]);

        Assert.True(result.Single(p => p.Purpose == "transactional").OptedIn);
        Assert.True(result.Single(p => p.Purpose == "service").OptedIn);
        Assert.False(result.Single(p => p.Purpose == "marketing").OptedIn);
    }

    [Fact]
    public void Resolve_AllScopeOptOut_OverridesEveryPurpose()
    {
        var result = ConsentStateResolver.Resolve(
            [("transactional", Day1), ("marketing", Day1), ("service", Day1)],
            [("all", Day2)]);

        Assert.All(result, p => Assert.False(p.OptedIn));
    }

    [Fact]
    public void Resolve_AllScopeOptOutBeforeSubsequentPurposeOptIn_PurposeIsOptedInAgain()
    {
        var result = ConsentStateResolver.Resolve(
            [("marketing", Day3)],
            [("all", Day2)]);

        Assert.True(result.Single(p => p.Purpose == "marketing").OptedIn);
    }

    [Fact]
    public void Resolve_ReturnsAllThreeCanonicalPurposesAlways()
    {
        var result = ConsentStateResolver.Resolve([], []);

        Assert.Equal(3, result.Count);
        Assert.Contains(result, p => p.Purpose == "transactional");
        Assert.Contains(result, p => p.Purpose == "marketing");
        Assert.Contains(result, p => p.Purpose == "service");
    }
}
