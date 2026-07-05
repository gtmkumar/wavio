using WaBilling.Application.Costs.Commands.RecordMessageCost;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace WaBilling.Tests.Costs;

public class RecordMessageCostCommandHandlerTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid PhoneNumberId = Guid.NewGuid();

    private static RecordMessageCostCommandHandler Handler(InMemoryWaBillingDbContext db) =>
        new(db, NullLogger<RecordMessageCostCommandHandler>.Instance);

    [Fact]
    public async Task HandleAsync_FirstDeliveryForAWamid_InsertsALedgerRowAndReturnsTrue()
    {
        await using var db = InMemoryWaBillingDbContext.Create(nameof(HandleAsync_FirstDeliveryForAWamid_InsertsALedgerRowAndReturnsTrue));

        var inserted = await Handler(db).HandleAsync(
            new RecordMessageCostCommand(
                TenantId, PhoneNumberId, "wamid.ABC", "marketing", "PMP", true, 0.87m, "INR", "IN", """{"billable":true}"""),
            CancellationToken.None);

        Assert.True(inserted);
        var row = Assert.Single(db.MessageCosts);
        Assert.Equal("wamid.ABC", row.Wamid);
        Assert.Equal("marketing", row.Category);
        Assert.Equal(0.87m, row.Amount);
        Assert.Equal("IN", row.DestinationMarket);
    }

    [Fact]
    public async Task HandleAsync_SameWamidDeliveredTwice_SecondCallIsSkippedNotDuplicated()
    {
        await using var db = InMemoryWaBillingDbContext.Create(nameof(HandleAsync_SameWamidDeliveredTwice_SecondCallIsSkippedNotDuplicated));
        var command = new RecordMessageCostCommand(
            TenantId, PhoneNumberId, "wamid.DUP", "utility", "PMP", true, 0.50m, "INR", "IN", null);

        var first = await Handler(db).HandleAsync(command, CancellationToken.None);
        var second = await Handler(db).HandleAsync(command, CancellationToken.None);

        Assert.True(first);
        Assert.False(second);
        Assert.Single(db.MessageCosts); // redelivered status webhook must not double-bill
    }

    [Fact]
    public async Task HandleAsync_StatusWithNoPricingCategory_SkipsWithoutInserting()
    {
        await using var db = InMemoryWaBillingDbContext.Create(nameof(HandleAsync_StatusWithNoPricingCategory_SkipsWithoutInserting));

        // e.g. a plain "sent" status update — Meta hasn't attached a pricing object yet.
        var inserted = await Handler(db).HandleAsync(
            new RecordMessageCostCommand(TenantId, PhoneNumberId, "wamid.SENT", null, null, null, null, null, null, null),
            CancellationToken.None);

        Assert.False(inserted);
        Assert.Empty(db.MessageCosts);
    }

    [Fact]
    public async Task HandleAsync_UnrecognizedPricingCategory_SkipsWithoutCrashing()
    {
        await using var db = InMemoryWaBillingDbContext.Create(nameof(HandleAsync_UnrecognizedPricingCategory_SkipsWithoutCrashing));

        // "referral_conversion" is a real Meta pricing.category value the CHECK constraint on
        // billing.message_costs.category does not (yet) accept — must not throw.
        var inserted = await Handler(db).HandleAsync(
            new RecordMessageCostCommand(
                TenantId, PhoneNumberId, "wamid.REFCONV", "referral_conversion", "PMP", true, 0m, "INR", "IN", null),
            CancellationToken.None);

        Assert.False(inserted);
        Assert.Empty(db.MessageCosts);
    }

    [Fact]
    public async Task HandleAsync_NonBillableDelivery_ZeroesTheAmountRegardlessOfReportedAmount()
    {
        await using var db = InMemoryWaBillingDbContext.Create(nameof(HandleAsync_NonBillableDelivery_ZeroesTheAmountRegardlessOfReportedAmount));

        await Handler(db).HandleAsync(
            new RecordMessageCostCommand(
                TenantId, PhoneNumberId, "wamid.FREE", "service", "PMP", false, 5.00m, "INR", "IN", null),
            CancellationToken.None);

        var row = Assert.Single(db.MessageCosts);
        Assert.False(row.Billable);
        Assert.Equal(0m, row.Amount);
    }

    [Fact]
    public async Task HandleAsync_BillableDelivery_UpsertsBothTheSpecificAndAllCategoryUsageCounters()
    {
        await using var db = InMemoryWaBillingDbContext.Create(nameof(HandleAsync_BillableDelivery_UpsertsBothTheSpecificAndAllCategoryUsageCounters));

        await Handler(db).HandleAsync(
            new RecordMessageCostCommand(
                TenantId, PhoneNumberId, "wamid.M1", "marketing", "PMP", true, 1.00m, "INR", "IN", null),
            CancellationToken.None);

        var marketingCounter = Assert.Single(db.UsageCounters, u => u.Category == "marketing");
        var allCounter = Assert.Single(db.UsageCounters, u => u.Category == "all");

        Assert.Equal(1, marketingCounter.MessageCount);
        Assert.Equal(1.00m, marketingCounter.BillableAmount);
        Assert.Equal(1, allCounter.MessageCount);
        Assert.Equal(1.00m, allCounter.BillableAmount);
    }

    [Fact]
    public async Task HandleAsync_TwoBillableDeliveriesSamePeriod_AccumulatesTheUsageCounter()
    {
        await using var db = InMemoryWaBillingDbContext.Create(nameof(HandleAsync_TwoBillableDeliveriesSamePeriod_AccumulatesTheUsageCounter));

        await Handler(db).HandleAsync(
            new RecordMessageCostCommand(TenantId, PhoneNumberId, "wamid.A", "utility", "PMP", true, 0.30m, "INR", "IN", null),
            CancellationToken.None);
        await Handler(db).HandleAsync(
            new RecordMessageCostCommand(TenantId, PhoneNumberId, "wamid.B", "utility", "PMP", true, 0.30m, "INR", "IN", null),
            CancellationToken.None);

        var counter = Assert.Single(db.UsageCounters, u => u.Category == "utility");
        Assert.Equal(2, counter.MessageCount);
        Assert.Equal(0.60m, counter.BillableAmount);
    }
}
