using WaGateway.Application.Campaigns.Commands.CancelCampaign;
using wavio.SharedDataModel.Entities.Messaging;
using wavio.Utilities.Exceptions;
using Xunit;

namespace WaGateway.Tests.Campaigns.Commands;

public class CancelCampaignCommandHandlerTests
{
    private static readonly Guid TenantId = Guid.NewGuid();

    private static Campaign SeedCampaign(InMemoryWaGatewayDbContext db, string status)
    {
        var now = DateTimeOffset.UtcNow;
        var campaign = new Campaign
        {
            Id = Guid.NewGuid(), TenantId = TenantId, PhoneNumberId = Guid.NewGuid(), Name = "Diwali sale",
            TemplateVersionId = Guid.NewGuid(), Status = status, AudienceCount = 3, CreatedAt = now, UpdatedAt = now, Version = 1,
        };
        db.Campaigns.Add(campaign);
        db.SaveChangesAsync(CancellationToken.None).GetAwaiter().GetResult();
        return campaign;
    }

    private static void SeedRecipient(InMemoryWaGatewayDbContext db, Guid campaignId, string waId, string status)
    {
        var now = DateTimeOffset.UtcNow;
        db.CampaignRecipients.Add(new CampaignRecipient
        {
            Id = Guid.NewGuid(), TenantId = TenantId, CampaignId = campaignId, WaId = waId, Status = status,
            CreatedAt = now, UpdatedAt = now, Version = 1,
        });
        db.SaveChangesAsync(CancellationToken.None).GetAwaiter().GetResult();
    }

    [Fact]
    public async Task Cancelling_a_running_campaign_marks_it_cancelled_and_cancels_pending_recipients_only()
    {
        await using var db = InMemoryWaGatewayDbContext.Create(nameof(Cancelling_a_running_campaign_marks_it_cancelled_and_cancels_pending_recipients_only));
        var campaign = SeedCampaign(db, "running");
        SeedRecipient(db, campaign.Id, "919876543210", "pending");
        SeedRecipient(db, campaign.Id, "919876543211", "sent");
        SeedRecipient(db, campaign.Id, "919876543212", "delivered");
        var handler = new CancelCampaignCommandHandler(db);

        var result = await handler.HandleAsync(new CancelCampaignCommand(TenantId, campaign.Id), CancellationToken.None);

        Assert.Equal("cancelled", result.Status);
        Assert.Equal("cancelled", db.CampaignRecipients.Single(r => r.WaId == "919876543210").Status);
        Assert.Equal("sent", db.CampaignRecipients.Single(r => r.WaId == "919876543211").Status); // untouched — already dispatched
        Assert.Equal("delivered", db.CampaignRecipients.Single(r => r.WaId == "919876543212").Status); // untouched
    }

    [Theory]
    [InlineData("completed")]
    [InlineData("cancelled")]
    [InlineData("failed")]
    public async Task Cancelling_an_already_terminal_campaign_is_rejected(string terminalStatus)
    {
        await using var db = InMemoryWaGatewayDbContext.Create($"{nameof(Cancelling_an_already_terminal_campaign_is_rejected)}_{terminalStatus}");
        var campaign = SeedCampaign(db, terminalStatus);
        var handler = new CancelCampaignCommandHandler(db);

        await Assert.ThrowsAsync<ValidationException>(() => handler.HandleAsync(new CancelCampaignCommand(TenantId, campaign.Id), CancellationToken.None));
    }

    [Fact]
    public async Task An_unknown_campaign_id_throws_KeyNotFoundException()
    {
        await using var db = InMemoryWaGatewayDbContext.Create(nameof(An_unknown_campaign_id_throws_KeyNotFoundException));
        var handler = new CancelCampaignCommandHandler(db);

        await Assert.ThrowsAsync<KeyNotFoundException>(() => handler.HandleAsync(new CancelCampaignCommand(TenantId, Guid.NewGuid()), CancellationToken.None));
    }
}
