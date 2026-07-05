using WaBilling.Application.RateCards.Commands.UpsertRateCard;
using WaBilling.Application.RateCards.Dtos;
using wavio.Utilities.Exceptions;
using Xunit;

namespace WaBilling.Tests.RateCards;

public class UpsertRateCardCommandHandlerTests
{
    private static UpsertRateCardRequest Request(
        DateOnly effectiveFrom, params UpsertRateCardEntryRequest[] entries) => new(
        "India rate card", "INR", "manual", effectiveFrom, null, "active", null, entries.ToList());

    [Fact]
    public async Task HandleAsync_NewCard_CreatesItWithItsEntries()
    {
        await using var db = InMemoryWaBillingDbContext.Create(nameof(HandleAsync_NewCard_CreatesItWithItsEntries));
        var handler = new UpsertRateCardCommandHandler(db);

        var result = await handler.HandleAsync(
            new UpsertRateCardCommand(null, Request(new DateOnly(2026, 7, 1),
                new UpsertRateCardEntryRequest("marketing", "IN", null, 0.78m)), null),
            CancellationToken.None);

        Assert.Equal("India rate card", result.Name);
        var entry = Assert.Single(result.Entries);
        Assert.Equal("marketing", entry.Category);
        Assert.Single(db.RateCards);
        Assert.Single(db.RateCardEntries);
    }

    [Fact]
    public async Task HandleAsync_CreatingASecondCardForTheSameCurrencyAndEffectiveFrom_ThrowsBusinessRuleException()
    {
        await using var db = InMemoryWaBillingDbContext.Create(
            nameof(HandleAsync_CreatingASecondCardForTheSameCurrencyAndEffectiveFrom_ThrowsBusinessRuleException));
        var handler = new UpsertRateCardCommandHandler(db);
        var effectiveFrom = new DateOnly(2026, 7, 1);

        await handler.HandleAsync(
            new UpsertRateCardCommand(null, Request(effectiveFrom, new UpsertRateCardEntryRequest("marketing", "IN", null, 0.78m)), null),
            CancellationToken.None);

        await Assert.ThrowsAsync<BusinessRuleException>(() => handler.HandleAsync(
            new UpsertRateCardCommand(null, Request(effectiveFrom, new UpsertRateCardEntryRequest("utility", "IN", null, 0.50m)), null),
            CancellationToken.None));
    }

    [Fact]
    public async Task HandleAsync_UpdatingAnExistingCard_FullyReplacesItsEntrySet()
    {
        await using var db = InMemoryWaBillingDbContext.Create(nameof(HandleAsync_UpdatingAnExistingCard_FullyReplacesItsEntrySet));
        var handler = new UpsertRateCardCommandHandler(db);

        var created = await handler.HandleAsync(
            new UpsertRateCardCommand(null, Request(new DateOnly(2026, 7, 1),
                new UpsertRateCardEntryRequest("marketing", "IN", null, 0.78m),
                new UpsertRateCardEntryRequest("utility", "IN", null, 0.50m)), null),
            CancellationToken.None);

        var updated = await handler.HandleAsync(
            new UpsertRateCardCommand(created.Id, Request(new DateOnly(2026, 7, 1),
                new UpsertRateCardEntryRequest("marketing", "IN", null, 0.85m)), null),
            CancellationToken.None);

        var entry = Assert.Single(updated.Entries);
        Assert.Equal("marketing", entry.Category);
        Assert.Equal(0.85m, entry.PricePerMessage);
        Assert.Single(db.RateCardEntries); // the old utility entry was removed, not left behind
    }

    [Fact]
    public async Task HandleAsync_UpdatingANonExistentCard_ThrowsBusinessRuleException()
    {
        await using var db = InMemoryWaBillingDbContext.Create(nameof(HandleAsync_UpdatingANonExistentCard_ThrowsBusinessRuleException));
        var handler = new UpsertRateCardCommandHandler(db);

        await Assert.ThrowsAsync<BusinessRuleException>(() => handler.HandleAsync(
            new UpsertRateCardCommand(Guid.NewGuid(), Request(new DateOnly(2026, 7, 1),
                new UpsertRateCardEntryRequest("marketing", "IN", null, 0.78m)), null),
            CancellationToken.None));
    }
}
