using WaBilling.Application.Estimator.Queries.EstimateCost;
using wavio.SharedDataModel.Entities.Billing;
using wavio.SharedDataModel.Entities.Waba;
using Xunit;

namespace WaBilling.Tests.Estimator;

public class EstimateCostQueryHandlerTests
{
    private static readonly Guid TenantId = Guid.NewGuid();

    private static EstimateCostQueryHandler Handler(InMemoryWaBillingDbContext db) => new(db);

    [Fact]
    public async Task HandleAsync_WindowOpen_IsAlwaysFreeRegardlessOfCategoryOrRateCards()
    {
        await using var db = InMemoryWaBillingDbContext.Create(nameof(HandleAsync_WindowOpen_IsAlwaysFreeRegardlessOfCategoryOrRateCards));

        var result = await Handler(db).HandleAsync(
            new EstimateCostQuery(TenantId, "marketing", "IN", WindowOpen: true, PhoneNumberId: null),
            CancellationToken.None);

        Assert.True(result.Found);
        Assert.False(result.Billable);
        Assert.Equal(0m, result.Amount);
    }

    [Fact]
    public async Task HandleAsync_NoActiveRateCard_ReturnsNotFound()
    {
        await using var db = InMemoryWaBillingDbContext.Create(nameof(HandleAsync_NoActiveRateCard_ReturnsNotFound));

        var result = await Handler(db).HandleAsync(
            new EstimateCostQuery(TenantId, "marketing", "IN", WindowOpen: false, PhoneNumberId: null),
            CancellationToken.None);

        Assert.False(result.Found);
    }

    [Fact]
    public async Task HandleAsync_ActiveCardWithMatchingEntry_ReturnsThePricedEstimate()
    {
        await using var db = InMemoryWaBillingDbContext.Create(nameof(HandleAsync_ActiveCardWithMatchingEntry_ReturnsThePricedEstimate));
        var card = new RateCard { Id = Guid.NewGuid(), Name = "Test card", Currency = "INR", EffectiveFrom = new DateOnly(2026, 1, 1), Status = "active" };
        db.RateCards.Add(card);
        db.RateCardEntries.Add(new RateCardEntry
        {
            Id = Guid.NewGuid(), RateCardId = card.Id, Category = "marketing", Market = "IN",
            VolumeTier = null, PricePerMessage = 0.78m, Currency = "INR",
        });
        await db.SaveChangesAsync(CancellationToken.None);

        var result = await Handler(db).HandleAsync(
            new EstimateCostQuery(TenantId, "marketing", "IN", WindowOpen: false, PhoneNumberId: null),
            CancellationToken.None);

        Assert.True(result.Found);
        Assert.True(result.Billable);
        Assert.Equal(0.78m, result.Amount);
        Assert.Equal(card.Id, result.RateCardId);
    }

    [Fact]
    public async Task HandleAsync_ActiveCardButNoEntryForThisCategoryMarket_ReturnsNotFound()
    {
        await using var db = InMemoryWaBillingDbContext.Create(nameof(HandleAsync_ActiveCardButNoEntryForThisCategoryMarket_ReturnsNotFound));
        var card = new RateCard { Id = Guid.NewGuid(), Name = "Test card", Currency = "INR", EffectiveFrom = new DateOnly(2026, 1, 1), Status = "active" };
        db.RateCards.Add(card);
        db.RateCardEntries.Add(new RateCardEntry
        {
            Id = Guid.NewGuid(), RateCardId = card.Id, Category = "marketing", Market = "IN",
            PricePerMessage = 0.78m, Currency = "INR",
        });
        await db.SaveChangesAsync(CancellationToken.None);

        var result = await Handler(db).HandleAsync(
            new EstimateCostQuery(TenantId, "utility", "IN", WindowOpen: false, PhoneNumberId: null),
            CancellationToken.None);

        Assert.False(result.Found);
    }

    [Fact]
    public async Task HandleAsync_FutureDatedCardNotYetEffective_UsesTheCurrentlyActiveCardInstead()
    {
        await using var db = InMemoryWaBillingDbContext.Create(nameof(HandleAsync_FutureDatedCardNotYetEffective_UsesTheCurrentlyActiveCardInstead));
        var current = new RateCard { Id = Guid.NewGuid(), Name = "Test card", Currency = "INR", EffectiveFrom = new DateOnly(2020, 1, 1), Status = "active" };
        var future = new RateCard { Id = Guid.NewGuid(), Name = "Test card", Currency = "INR", EffectiveFrom = DateOnly.FromDateTime(DateTime.UtcNow.AddYears(1)), Status = "draft" };
        db.RateCards.AddRange(current, future);
        db.RateCardEntries.Add(new RateCardEntry { Id = Guid.NewGuid(), RateCardId = current.Id, Category = "marketing", Market = "IN", PricePerMessage = 0.75m, Currency = "INR" });
        db.RateCardEntries.Add(new RateCardEntry { Id = Guid.NewGuid(), RateCardId = future.Id, Category = "marketing", Market = "IN", PricePerMessage = 0.99m, Currency = "INR" });
        await db.SaveChangesAsync(CancellationToken.None);

        var result = await Handler(db).HandleAsync(
            new EstimateCostQuery(TenantId, "marketing", "IN", WindowOpen: false, PhoneNumberId: null),
            CancellationToken.None);

        Assert.Equal(current.Id, result.RateCardId);
        Assert.Equal(0.75m, result.Amount);
    }

    [Fact]
    public async Task HandleAsync_UtilityWithOwnedPhoneNumberTier_PrefersTheTierSpecificEntry()
    {
        await using var db = InMemoryWaBillingDbContext.Create(nameof(HandleAsync_UtilityWithOwnedPhoneNumberTier_PrefersTheTierSpecificEntry));
        var phoneNumber = new WabaPhoneNumber
        {
            Id = Guid.NewGuid(), TenantId = TenantId, MetaPhoneNumberId = "meta-1", DisplayPhoneNumber = "+911234567890",
            Status = "CONNECTED", MessagingTier = "TIER_10K",
        };
        db.WabaPhoneNumbers.Add(phoneNumber);
        var card = new RateCard { Id = Guid.NewGuid(), Name = "Test card", Currency = "INR", EffectiveFrom = new DateOnly(2026, 1, 1), Status = "active" };
        db.RateCards.Add(card);
        db.RateCardEntries.Add(new RateCardEntry { Id = Guid.NewGuid(), RateCardId = card.Id, Category = "utility", Market = "IN", VolumeTier = null, PricePerMessage = 0.60m, Currency = "INR" });
        db.RateCardEntries.Add(new RateCardEntry { Id = Guid.NewGuid(), RateCardId = card.Id, Category = "utility", Market = "IN", VolumeTier = "TIER_10K", PricePerMessage = 0.35m, Currency = "INR" });
        await db.SaveChangesAsync(CancellationToken.None);

        var result = await Handler(db).HandleAsync(
            new EstimateCostQuery(TenantId, "utility", "IN", WindowOpen: false, PhoneNumberId: phoneNumber.Id),
            CancellationToken.None);

        Assert.Equal(0.35m, result.Amount);
        Assert.Equal("TIER_10K", result.VolumeTier);
    }

    [Fact]
    public async Task HandleAsync_MarketingIgnoresAnyPhoneNumberTier_NoVolumeDiscounts()
    {
        await using var db = InMemoryWaBillingDbContext.Create(nameof(HandleAsync_MarketingIgnoresAnyPhoneNumberTier_NoVolumeDiscounts));
        var phoneNumber = new WabaPhoneNumber
        {
            Id = Guid.NewGuid(), TenantId = TenantId, MetaPhoneNumberId = "meta-2", DisplayPhoneNumber = "+911234567891",
            Status = "CONNECTED", MessagingTier = "TIER_10K",
        };
        db.WabaPhoneNumbers.Add(phoneNumber);
        var card = new RateCard { Id = Guid.NewGuid(), Name = "Test card", Currency = "INR", EffectiveFrom = new DateOnly(2026, 1, 1), Status = "active" };
        db.RateCards.Add(card);
        db.RateCardEntries.Add(new RateCardEntry { Id = Guid.NewGuid(), RateCardId = card.Id, Category = "marketing", Market = "IN", VolumeTier = null, PricePerMessage = 0.80m, Currency = "INR" });
        // Even if a tier-specific marketing row existed, it must never be selected.
        db.RateCardEntries.Add(new RateCardEntry { Id = Guid.NewGuid(), RateCardId = card.Id, Category = "marketing", Market = "IN", VolumeTier = "TIER_10K", PricePerMessage = 0.10m, Currency = "INR" });
        await db.SaveChangesAsync(CancellationToken.None);

        var result = await Handler(db).HandleAsync(
            new EstimateCostQuery(TenantId, "marketing", "IN", WindowOpen: false, PhoneNumberId: phoneNumber.Id),
            CancellationToken.None);

        Assert.Equal(0.80m, result.Amount);
        Assert.Null(result.VolumeTier);
    }
}
