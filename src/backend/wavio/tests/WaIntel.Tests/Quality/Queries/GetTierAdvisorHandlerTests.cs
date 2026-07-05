using WaIntel.Application.Quality.Queries.GetTierAdvisor;
using wavio.SharedDataModel.Entities.Quality;
using wavio.SharedDataModel.Entities.Waba;
using Xunit;

namespace WaIntel.Tests.Quality.Queries;

public class GetTierAdvisorHandlerTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid PhoneNumberId = Guid.NewGuid();

    [Fact]
    public async Task HandleAsync_UnknownPhoneNumber_ReturnsNull()
    {
        await using var db = InMemoryWaIntelDbContext.Create(nameof(HandleAsync_UnknownPhoneNumber_ReturnsNull));
        var handler = new GetTierAdvisorHandler(db);

        var result = await handler.HandleAsync(new GetTierAdvisorQuery(TenantId, PhoneNumberId), CancellationToken.None);

        Assert.Null(result);
    }

    [Fact]
    public async Task HandleAsync_NoMessagingTierReportedYet_ReturnsNull()
    {
        await using var db = InMemoryWaIntelDbContext.Create(nameof(HandleAsync_NoMessagingTierReportedYet_ReturnsNull));
        db.WabaPhoneNumbers.Add(PhoneNumber(messagingTier: null));
        await db.SaveChangesAsync(CancellationToken.None);

        var handler = new GetTierAdvisorHandler(db);
        var result = await handler.HandleAsync(new GetTierAdvisorQuery(TenantId, PhoneNumberId), CancellationToken.None);

        Assert.Null(result);
    }

    [Fact]
    public async Task HandleAsync_NoSnapshotYet_UsesZeroVolumeAndRecommendsWaiting()
    {
        await using var db = InMemoryWaIntelDbContext.Create(nameof(HandleAsync_NoSnapshotYet_UsesZeroVolumeAndRecommendsWaiting));
        db.WabaPhoneNumbers.Add(PhoneNumber(messagingTier: "TIER_1K"));
        await db.SaveChangesAsync(CancellationToken.None);

        var handler = new GetTierAdvisorHandler(db);
        var result = await handler.HandleAsync(new GetTierAdvisorQuery(TenantId, PhoneNumberId), CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal(0, result!.RecentAverageDailyVolume);
        Assert.False(result.ReadyToGrow);
    }

    [Fact]
    public async Task HandleAsync_UsesLatestSnapshotsWeeklyVolumeDividedBySeven()
    {
        await using var db = InMemoryWaIntelDbContext.Create(nameof(HandleAsync_UsesLatestSnapshotsWeeklyVolumeDividedBySeven));
        db.WabaPhoneNumbers.Add(PhoneNumber(messagingTier: "TIER_1K", qualityRating: "GREEN"));
        db.HealthSnapshots.Add(new HealthSnapshot
        {
            Id = Guid.NewGuid(),
            TenantId = TenantId,
            PhoneNumberId = PhoneNumberId,
            PeriodStart = new DateOnly(2026, 6, 22),
            PeriodEnd = new DateOnly(2026, 6, 28),
            MessagesSent = 6_300, // 900/day
            CreatedAt = DateTimeOffset.UtcNow,
        });
        await db.SaveChangesAsync(CancellationToken.None);

        var handler = new GetTierAdvisorHandler(db);
        var result = await handler.HandleAsync(new GetTierAdvisorQuery(TenantId, PhoneNumberId), CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal(900, result!.RecentAverageDailyVolume);
        Assert.True(result.ReadyToGrow); // 900/1000 = 90%, above the 80% growth threshold
    }

    private static WabaPhoneNumber PhoneNumber(string? messagingTier, string? qualityRating = "GREEN") => new()
    {
        Id = PhoneNumberId,
        TenantId = TenantId,
        BusinessAccountId = Guid.NewGuid(),
        MetaPhoneNumberId = "1234567890",
        DisplayPhoneNumber = "+1 234 567 890",
        Status = "CONNECTED",
        MessagingTier = messagingTier,
        QualityRating = qualityRating,
        CreatedAt = DateTimeOffset.UtcNow,
        UpdatedAt = DateTimeOffset.UtcNow,
        Version = 1,
    };
}
