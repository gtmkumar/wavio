using WaGateway.Application.Campaigns.Queries.GetCampaign;
using wavio.SharedDataModel.Entities.Messaging;
using Xunit;

namespace WaGateway.Tests.Campaigns.Queries;

public class GetCampaignQueryHandlerTests
{
    private static readonly Guid TenantId = Guid.NewGuid();

    private static Campaign SeedCampaign(InMemoryWaGatewayDbContext db)
    {
        var now = DateTimeOffset.UtcNow;
        var campaign = new Campaign
        {
            Id = Guid.NewGuid(), TenantId = TenantId, PhoneNumberId = Guid.NewGuid(), Name = "Diwali sale",
            TemplateVersionId = Guid.NewGuid(), Status = "running", AudienceCount = 4,
            CreatedAt = now, UpdatedAt = now, Version = 1,
        };
        db.Campaigns.Add(campaign);
        db.SaveChangesAsync(CancellationToken.None).GetAwaiter().GetResult();
        return campaign;
    }

    private static void SeedRecipient(InMemoryWaGatewayDbContext db, Guid campaignId, string waId, string status, string? errorCode = null)
    {
        var now = DateTimeOffset.UtcNow;
        db.CampaignRecipients.Add(new CampaignRecipient
        {
            Id = Guid.NewGuid(), TenantId = TenantId, CampaignId = campaignId, WaId = waId, Status = status,
            ErrorCode = errorCode, CreatedAt = now, UpdatedAt = now, Version = 1,
        });
        db.SaveChangesAsync(CancellationToken.None).GetAwaiter().GetResult();
    }

    [Fact]
    public async Task Groups_failed_recipients_by_error_code_into_the_failure_breakdown()
    {
        await using var db = InMemoryWaGatewayDbContext.Create(nameof(Groups_failed_recipients_by_error_code_into_the_failure_breakdown));
        var campaign = SeedCampaign(db);
        SeedRecipient(db, campaign.Id, "1", "failed", "TIER_EXHAUSTED");
        SeedRecipient(db, campaign.Id, "2", "failed", "TIER_EXHAUSTED");
        SeedRecipient(db, campaign.Id, "3", "failed", "WINDOW_CLOSED");
        SeedRecipient(db, campaign.Id, "4", "delivered");
        var handler = new GetCampaignQueryHandler(db);

        var result = await handler.HandleAsync(new GetCampaignQuery(TenantId, campaign.Id), CancellationToken.None);

        Assert.NotNull(result.FailureBreakdown);
        Assert.Equal(2, result.FailureBreakdown!["TIER_EXHAUSTED"]);
        Assert.Equal(1, result.FailureBreakdown!["WINDOW_CLOSED"]);
        Assert.Equal(2, result.FailureBreakdown!.Count);
    }

    [Fact]
    public async Task An_unknown_campaign_id_throws_KeyNotFoundException()
    {
        await using var db = InMemoryWaGatewayDbContext.Create(nameof(An_unknown_campaign_id_throws_KeyNotFoundException));
        var handler = new GetCampaignQueryHandler(db);

        await Assert.ThrowsAsync<KeyNotFoundException>(() => handler.HandleAsync(new GetCampaignQuery(TenantId, Guid.NewGuid()), CancellationToken.None));
    }

    [Fact]
    public async Task A_campaign_belonging_to_another_tenant_throws_KeyNotFoundException()
    {
        await using var db = InMemoryWaGatewayDbContext.Create(nameof(A_campaign_belonging_to_another_tenant_throws_KeyNotFoundException));
        var campaign = SeedCampaign(db);
        var handler = new GetCampaignQueryHandler(db);

        await Assert.ThrowsAsync<KeyNotFoundException>(() => handler.HandleAsync(new GetCampaignQuery(Guid.NewGuid(), campaign.Id), CancellationToken.None));
    }
}
