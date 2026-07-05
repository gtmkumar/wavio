using WaIntel.Application.Quality.Commands.RecordTierChange;
using WaIntel.Application.Quality.Logic;
using wavio.SharedDataModel.Entities.Waba;
using WaPlatform.Contracts.IntegrationEvents.V1;
using Xunit;

namespace WaIntel.Tests.Quality.Commands;

public class RecordTierChangeHandlerTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid PhoneNumberId = Guid.NewGuid();

    private static async Task<InMemoryWaIntelDbContext> SeedPhoneNumberAsync(string testName, string? currentTier)
    {
        var db = InMemoryWaIntelDbContext.Create(testName);
        db.WabaPhoneNumbers.Add(new WabaPhoneNumber
        {
            Id = PhoneNumberId,
            TenantId = TenantId,
            BusinessAccountId = Guid.NewGuid(),
            MetaPhoneNumberId = "1234567890",
            DisplayPhoneNumber = "+1 234 567 890",
            Status = "CONNECTED",
            MessagingTier = currentTier,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
            Version = 1,
        });
        await db.SaveChangesAsync(CancellationToken.None);
        return db;
    }

    [Fact]
    public async Task HandleAsync_Upgrade_RecordsEventButOpensNoIncident()
    {
        await using var db = await SeedPhoneNumberAsync(nameof(HandleAsync_Upgrade_RecordsEventButOpensNoIncident), "TIER_1K");
        var publisher = new FakeEventBusPublisher();
        var handler = new RecordTierChangeHandler(db, publisher);

        var result = await handler.HandleAsync(
            new RecordTierChangeCommand(TenantId, PhoneNumberId, "waba-1", null, "TIER_10K", "webhook", null),
            CancellationToken.None);

        Assert.Null(result);
        Assert.Empty(db.GuardianIncidents);
        var tierEvent = Assert.Single(db.MessagingTierEvents);
        Assert.Equal("tier_1k", tierEvent.OldTier);
        Assert.Equal("tier_10k", tierEvent.NewTier);
        Assert.Equal("TIER_10K", db.WabaPhoneNumbers.Single().MessagingTier);
    }

    [Fact]
    public async Task HandleAsync_Downgrade_OpensTierDowngradeIncidentWithNoThrottle()
    {
        await using var db = await SeedPhoneNumberAsync(nameof(HandleAsync_Downgrade_OpensTierDowngradeIncidentWithNoThrottle), "TIER_10K");
        var publisher = new FakeEventBusPublisher();
        var handler = new RecordTierChangeHandler(db, publisher);

        var result = await handler.HandleAsync(
            new RecordTierChangeCommand(TenantId, PhoneNumberId, "waba-1", null, "TIER_1K", "webhook", null),
            CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal(GuardianRules.IncidentTierDowngrade, result!.IncidentType);
        Assert.Equal(GuardianRules.ThrottleNone, result.ThrottleAction);
        Assert.Single(publisher.Published.OfType<GuardianIncidentOpenedV1>());
    }

    [Fact]
    public async Task HandleAsync_UnrecognizedTierCode_StoresRawValueButWritesNoEventRow()
    {
        await using var db = await SeedPhoneNumberAsync(nameof(HandleAsync_UnrecognizedTierCode_StoresRawValueButWritesNoEventRow), "TIER_1K");
        var publisher = new FakeEventBusPublisher();
        var handler = new RecordTierChangeHandler(db, publisher);

        var result = await handler.HandleAsync(
            new RecordTierChangeCommand(TenantId, PhoneNumberId, "waba-1", null, "TIER_BRAND_NEW", "webhook", null),
            CancellationToken.None);

        Assert.Null(result);
        Assert.Empty(db.MessagingTierEvents);
        Assert.Equal("TIER_BRAND_NEW", db.WabaPhoneNumbers.Single().MessagingTier);
    }

    [Fact]
    public async Task HandleAsync_NoActualChange_IsANoOp()
    {
        await using var db = await SeedPhoneNumberAsync(nameof(HandleAsync_NoActualChange_IsANoOp), "TIER_1K");
        var publisher = new FakeEventBusPublisher();
        var handler = new RecordTierChangeHandler(db, publisher);

        var result = await handler.HandleAsync(
            new RecordTierChangeCommand(TenantId, PhoneNumberId, "waba-1", null, "TIER_1K", "webhook", null),
            CancellationToken.None);

        Assert.Null(result);
        Assert.Empty(db.MessagingTierEvents);
    }
}
