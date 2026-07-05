using WaAdmin.Application.Consent.Queries.GetConsentState;
using WaAdmin.Tests.Fakes;
using wavio.SharedDataModel.Entities.Consent;
using wavio.SharedDataModel.Entities.Messaging;
using Xunit;

namespace WaAdmin.Tests.Consent;

public class GetConsentStateQueryHandlerTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private const string WaId = "919876543210";

    private static InMemoryWaAdminDbContext NewDb([System.Runtime.CompilerServices.CallerMemberName] string name = "") =>
        InMemoryWaAdminDbContext.Create(name);

    [Fact]
    public async Task HandleAsync_NoEvents_ReturnsNotSuppressedAndAllPurposesOptedOut()
    {
        await using var db = NewDb();
        var handler = new GetConsentStateQueryHandler(db);

        var result = await handler.HandleAsync(new GetConsentStateQuery(TenantId, WaId), CancellationToken.None);

        Assert.False(result.Suppressed);
        Assert.All(result.Purposes, p => Assert.False(p.OptedIn));
    }

    [Fact]
    public async Task HandleAsync_MarketingOptInThenSuppressed_ReportsSuppressedTrue()
    {
        await using var db = NewDb();
        var now = DateTimeOffset.UtcNow;
        db.OptInEvents.Add(new OptInEvent
        {
            Id = Guid.NewGuid(), TenantId = TenantId, WaId = WaId, Purpose = "marketing",
            CaptureChannel = "web_form", OccurredAt = now, CreatedAt = now,
        });
        db.SuppressionListEntries.Add(new SuppressionListEntry
        {
            Id = Guid.NewGuid(), TenantId = TenantId, WaId = WaId, Reason = "stop_keyword",
            CreatedAt = now, UpdatedAt = now,
        });
        await db.SaveChangesAsync(CancellationToken.None);
        var handler = new GetConsentStateQueryHandler(db);

        var result = await handler.HandleAsync(new GetConsentStateQuery(TenantId, WaId), CancellationToken.None);

        Assert.True(result.Suppressed);
        // Consent-state resolution (opt-in vs opt-out event history) is independent of the
        // suppression flag — GetConsentStateQueryHandler never wrote an opt_out_event here, so the
        // marketing purpose itself still reads as opted-in even while suppressed.
        Assert.True(result.Purposes.Single(p => p.Purpose == "marketing").OptedIn);
    }

    [Fact]
    public async Task HandleAsync_ExpiredSuppressionEntry_ReportsSuppressedFalse()
    {
        await using var db = NewDb();
        var now = DateTimeOffset.UtcNow;
        db.SuppressionListEntries.Add(new SuppressionListEntry
        {
            Id = Guid.NewGuid(), TenantId = TenantId, WaId = WaId, Reason = "manual",
            ExpiresAt = now.AddDays(-1), CreatedAt = now, UpdatedAt = now,
        });
        await db.SaveChangesAsync(CancellationToken.None);
        var handler = new GetConsentStateQueryHandler(db);

        var result = await handler.HandleAsync(new GetConsentStateQuery(TenantId, WaId), CancellationToken.None);

        Assert.False(result.Suppressed);
    }

    [Fact]
    public async Task HandleAsync_AnotherTenantsEventsForSameWaId_AreNotVisible()
    {
        await using var db = NewDb();
        var otherTenantId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;
        db.OptInEvents.Add(new OptInEvent
        {
            Id = Guid.NewGuid(), TenantId = otherTenantId, WaId = WaId, Purpose = "marketing",
            CaptureChannel = "web_form", OccurredAt = now, CreatedAt = now,
        });
        await db.SaveChangesAsync(CancellationToken.None);
        var handler = new GetConsentStateQueryHandler(db);

        var result = await handler.HandleAsync(new GetConsentStateQuery(TenantId, WaId), CancellationToken.None);

        Assert.False(result.Purposes.Single(p => p.Purpose == "marketing").OptedIn);
    }
}
