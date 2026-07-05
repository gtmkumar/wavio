using WaGateway.Application.Campaigns.Commands.LaunchCampaign;
using wavio.SharedDataModel.Entities.Messaging;
using wavio.SharedDataModel.Entities.Templates;
using wavio.Utilities.Exceptions;
using Xunit;

namespace WaGateway.Tests.Campaigns.Commands;

public class LaunchCampaignCommandHandlerTests
{
    private static readonly Guid TenantId = Guid.NewGuid();

    private static Campaign SeedCampaign(InMemoryWaGatewayDbContext db, string status, Guid? templateVersionId = null)
    {
        var now = DateTimeOffset.UtcNow;
        var campaign = new Campaign
        {
            Id = Guid.NewGuid(),
            TenantId = TenantId,
            PhoneNumberId = Guid.NewGuid(),
            Name = "Diwali sale",
            TemplateVersionId = templateVersionId ?? Guid.NewGuid(),
            Status = status,
            AudienceCount = 1,
            CreatedAt = now,
            UpdatedAt = now,
            Version = 1,
        };
        db.Campaigns.Add(campaign);
        db.SaveChangesAsync(CancellationToken.None).GetAwaiter().GetResult();
        return campaign;
    }

    private static void SeedTemplate(InMemoryWaGatewayDbContext db, Guid templateVersionId, string templateStatus)
    {
        var templateId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;
        db.Templates.Add(new Template
        {
            Id = templateId, TenantId = TenantId, BusinessAccountId = Guid.NewGuid(), Name = "promo",
            Language = "en_US", Category = "marketing", Status = templateStatus, CreatedAt = now, UpdatedAt = now, Version = 1,
        });
        db.TemplateVersions.Add(new TemplateVersion
        {
            Id = templateVersionId, TenantId = TenantId, TemplateId = templateId, VersionNumber = 1,
            Components = "[]", Status = "APPROVED", CreatedAt = now, UpdatedAt = now,
        });
        db.SaveChangesAsync(CancellationToken.None).GetAwaiter().GetResult();
    }

    [Fact]
    public async Task A_draft_campaign_transitions_to_running_and_sets_started_at()
    {
        await using var db = InMemoryWaGatewayDbContext.Create(nameof(A_draft_campaign_transitions_to_running_and_sets_started_at));
        var templateVersionId = Guid.NewGuid();
        SeedTemplate(db, templateVersionId, "APPROVED");
        var campaign = SeedCampaign(db, "draft", templateVersionId);
        var handler = new LaunchCampaignCommandHandler(db);

        var result = await handler.HandleAsync(new LaunchCampaignCommand(TenantId, campaign.Id), CancellationToken.None);

        Assert.Equal("running", result.Status);
        Assert.NotNull(result.StartedAt);
    }

    [Fact]
    public async Task A_scheduled_campaign_can_also_be_launched_directly()
    {
        await using var db = InMemoryWaGatewayDbContext.Create(nameof(A_scheduled_campaign_can_also_be_launched_directly));
        var templateVersionId = Guid.NewGuid();
        SeedTemplate(db, templateVersionId, "APPROVED");
        var campaign = SeedCampaign(db, "scheduled", templateVersionId);
        var handler = new LaunchCampaignCommandHandler(db);

        var result = await handler.HandleAsync(new LaunchCampaignCommand(TenantId, campaign.Id), CancellationToken.None);

        Assert.Equal("running", result.Status);
    }

    [Fact]
    public async Task An_already_running_campaign_cannot_be_relaunched()
    {
        await using var db = InMemoryWaGatewayDbContext.Create(nameof(An_already_running_campaign_cannot_be_relaunched));
        var campaign = SeedCampaign(db, "running");
        var handler = new LaunchCampaignCommandHandler(db);

        await Assert.ThrowsAsync<ValidationException>(() => handler.HandleAsync(new LaunchCampaignCommand(TenantId, campaign.Id), CancellationToken.None));
    }

    [Fact]
    public async Task A_completed_campaign_cannot_be_relaunched()
    {
        await using var db = InMemoryWaGatewayDbContext.Create(nameof(A_completed_campaign_cannot_be_relaunched));
        var campaign = SeedCampaign(db, "completed");
        var handler = new LaunchCampaignCommandHandler(db);

        await Assert.ThrowsAsync<ValidationException>(() => handler.HandleAsync(new LaunchCampaignCommand(TenantId, campaign.Id), CancellationToken.None));
    }

    [Fact]
    public async Task Launching_against_a_DISABLED_template_is_rejected()
    {
        await using var db = InMemoryWaGatewayDbContext.Create(nameof(Launching_against_a_DISABLED_template_is_rejected));
        var templateVersionId = Guid.NewGuid();
        SeedTemplate(db, templateVersionId, "DISABLED");
        var campaign = SeedCampaign(db, "draft", templateVersionId);
        var handler = new LaunchCampaignCommandHandler(db);

        await Assert.ThrowsAsync<ValidationException>(() => handler.HandleAsync(new LaunchCampaignCommand(TenantId, campaign.Id), CancellationToken.None));
    }

    [Fact]
    public async Task Launching_against_a_PAUSED_template_is_allowed_the_chunker_holds_it_back_instead()
    {
        await using var db = InMemoryWaGatewayDbContext.Create(nameof(Launching_against_a_PAUSED_template_is_allowed_the_chunker_holds_it_back_instead));
        var templateVersionId = Guid.NewGuid();
        SeedTemplate(db, templateVersionId, "PAUSED");
        var campaign = SeedCampaign(db, "draft", templateVersionId);
        var handler = new LaunchCampaignCommandHandler(db);

        var result = await handler.HandleAsync(new LaunchCampaignCommand(TenantId, campaign.Id), CancellationToken.None);

        Assert.Equal("running", result.Status);
    }

    [Fact]
    public async Task An_unknown_campaign_id_throws_KeyNotFoundException()
    {
        await using var db = InMemoryWaGatewayDbContext.Create(nameof(An_unknown_campaign_id_throws_KeyNotFoundException));
        var handler = new LaunchCampaignCommandHandler(db);

        await Assert.ThrowsAsync<KeyNotFoundException>(() => handler.HandleAsync(new LaunchCampaignCommand(TenantId, Guid.NewGuid()), CancellationToken.None));
    }
}
