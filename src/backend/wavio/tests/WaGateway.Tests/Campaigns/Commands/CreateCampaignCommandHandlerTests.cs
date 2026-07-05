using WaGateway.Application.Campaigns.Commands.CreateCampaign;
using WaGateway.Application.Campaigns.Dtos;
using WaGateway.Application.Common.Interfaces;
using Moq;
using wavio.SharedDataModel.Entities.Messaging;
using wavio.SharedDataModel.Entities.Templates;
using wavio.SharedDataModel.Entities.Waba;
using wavio.Utilities.Exceptions;
using Xunit;

namespace WaGateway.Tests.Campaigns.Commands;

public class CreateCampaignCommandHandlerTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid PhoneNumberId = Guid.NewGuid();

    private static void SeedPhoneNumber(InMemoryWaGatewayDbContext db, Guid tenantId, Guid phoneNumberId)
    {
        db.WabaPhoneNumbers.Add(new WabaPhoneNumber
        {
            Id = phoneNumberId,
            TenantId = tenantId,
            BusinessAccountId = Guid.NewGuid(),
            MetaPhoneNumberId = phoneNumberId.ToString("N"),
            DisplayPhoneNumber = "+1 555 0100",
            Status = "CONNECTED",
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
            Version = 1,
        });
        db.SaveChangesAsync(CancellationToken.None).GetAwaiter().GetResult();
    }

    private static (Guid TemplateId, Guid VersionId) SeedApprovedTemplate(
        InMemoryWaGatewayDbContext db, Guid tenantId, string category = "marketing", string templateStatus = "APPROVED", string versionStatus = "APPROVED")
    {
        var templateId = Guid.NewGuid();
        var versionId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;
        db.Templates.Add(new Template
        {
            Id = templateId,
            TenantId = tenantId,
            BusinessAccountId = Guid.NewGuid(),
            Name = "promo",
            Language = "en_US",
            Category = category,
            Status = templateStatus,
            CreatedAt = now,
            UpdatedAt = now,
            Version = 1,
        });
        db.TemplateVersions.Add(new TemplateVersion
        {
            Id = versionId,
            TenantId = tenantId,
            TemplateId = templateId,
            VersionNumber = 1,
            Components = """[{"type":"BODY","text":"Hi {{1}}"}]""",
            Status = versionStatus,
            CreatedAt = now,
            UpdatedAt = now,
        });
        db.SaveChangesAsync(CancellationToken.None).GetAwaiter().GetResult();
        return (templateId, versionId);
    }

    private static Mock<IBillingEstimatorClient> Estimator(BillingEstimateResult? result = null)
    {
        var mock = new Mock<IBillingEstimatorClient>();
        mock.Setup(e => e.EstimateAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(result);
        return mock;
    }

    [Fact]
    public async Task Creates_a_campaign_with_one_pending_recipient_per_audience_member()
    {
        await using var db = InMemoryWaGatewayDbContext.Create(nameof(Creates_a_campaign_with_one_pending_recipient_per_audience_member));
        SeedPhoneNumber(db, TenantId, PhoneNumberId);
        var (_, versionId) = SeedApprovedTemplate(db, TenantId);
        var handler = new CreateCampaignCommandHandler(db, Estimator().Object);

        var result = await handler.HandleAsync(
            new CreateCampaignCommand(
                TenantId, "Diwali sale", PhoneNumberId, versionId, null,
                [new CampaignAudienceMemberRequest("919876543210", null), new CampaignAudienceMemberRequest("919876543211", null)],
                null, null),
            CancellationToken.None);

        Assert.Equal("draft", result.Status);
        Assert.Equal(2, result.AudienceCount);
        Assert.Equal(0, result.SuppressedCount);
        Assert.Equal(2, db.CampaignRecipients.Count());
        Assert.All(db.CampaignRecipients, r => Assert.Equal("pending", r.Status));
    }

    [Fact]
    public async Task Marks_a_suppressed_recipient_up_front_for_a_marketing_template()
    {
        await using var db = InMemoryWaGatewayDbContext.Create(nameof(Marks_a_suppressed_recipient_up_front_for_a_marketing_template));
        SeedPhoneNumber(db, TenantId, PhoneNumberId);
        var (_, versionId) = SeedApprovedTemplate(db, TenantId, category: "marketing");
        db.SuppressionListEntries.Add(new SuppressionListEntry
        {
            Id = Guid.NewGuid(), TenantId = TenantId, WaId = "919876543210", Reason = "stop_keyword",
            CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow,
        });
        await db.SaveChangesAsync(CancellationToken.None);
        var handler = new CreateCampaignCommandHandler(db, Estimator().Object);

        var result = await handler.HandleAsync(
            new CreateCampaignCommand(
                TenantId, "Diwali sale", PhoneNumberId, versionId, null,
                [new CampaignAudienceMemberRequest("919876543210", null), new CampaignAudienceMemberRequest("919876543211", null)],
                null, null),
            CancellationToken.None);

        Assert.Equal(1, result.SuppressedCount);
        Assert.Equal("suppressed", db.CampaignRecipients.Single(r => r.WaId == "919876543210").Status);
        Assert.Equal("pending", db.CampaignRecipients.Single(r => r.WaId == "919876543211").Status);
    }

    [Fact]
    public async Task Does_not_suppress_recipients_for_a_utility_template()
    {
        // Suppression means "no MARKETING" specifically (spec §4.10) — same rule SendMessageHandler
        // enforces for ad hoc sends (issue #21).
        await using var db = InMemoryWaGatewayDbContext.Create(nameof(Does_not_suppress_recipients_for_a_utility_template));
        SeedPhoneNumber(db, TenantId, PhoneNumberId);
        var (_, versionId) = SeedApprovedTemplate(db, TenantId, category: "utility");
        db.SuppressionListEntries.Add(new SuppressionListEntry
        {
            Id = Guid.NewGuid(), TenantId = TenantId, WaId = "919876543210", Reason = "stop_keyword",
            CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow,
        });
        await db.SaveChangesAsync(CancellationToken.None);
        var handler = new CreateCampaignCommandHandler(db, Estimator().Object);

        var result = await handler.HandleAsync(
            new CreateCampaignCommand(
                TenantId, "OTP reminders", PhoneNumberId, versionId, null,
                [new CampaignAudienceMemberRequest("919876543210", null)], null, null),
            CancellationToken.None);

        Assert.Equal(0, result.SuppressedCount);
        Assert.Equal("pending", db.CampaignRecipients.Single().Status);
    }

    [Fact]
    public async Task Rejects_a_duplicate_waId_in_the_audience()
    {
        await using var db = InMemoryWaGatewayDbContext.Create(nameof(Rejects_a_duplicate_waId_in_the_audience));
        SeedPhoneNumber(db, TenantId, PhoneNumberId);
        var (_, versionId) = SeedApprovedTemplate(db, TenantId);
        var handler = new CreateCampaignCommandHandler(db, Estimator().Object);

        await Assert.ThrowsAsync<ValidationException>(() => handler.HandleAsync(
            new CreateCampaignCommand(
                TenantId, "Diwali sale", PhoneNumberId, versionId, null,
                [new CampaignAudienceMemberRequest("919876543210", null), new CampaignAudienceMemberRequest("919876543210", null)],
                null, null),
            CancellationToken.None));

        Assert.Empty(db.Campaigns);
    }

    [Fact]
    public async Task Rejects_an_empty_audience()
    {
        await using var db = InMemoryWaGatewayDbContext.Create(nameof(Rejects_an_empty_audience));
        SeedPhoneNumber(db, TenantId, PhoneNumberId);
        var (_, versionId) = SeedApprovedTemplate(db, TenantId);
        var handler = new CreateCampaignCommandHandler(db, Estimator().Object);

        await Assert.ThrowsAsync<ValidationException>(() => handler.HandleAsync(
            new CreateCampaignCommand(TenantId, "Diwali sale", PhoneNumberId, versionId, null, [], null, null),
            CancellationToken.None));
    }

    [Fact]
    public async Task A_phone_number_id_with_no_matching_row_throws_KeyNotFoundException()
    {
        await using var db = InMemoryWaGatewayDbContext.Create(nameof(A_phone_number_id_with_no_matching_row_throws_KeyNotFoundException));
        var (_, versionId) = SeedApprovedTemplate(db, TenantId);
        var handler = new CreateCampaignCommandHandler(db, Estimator().Object);

        await Assert.ThrowsAsync<KeyNotFoundException>(() => handler.HandleAsync(
            new CreateCampaignCommand(
                TenantId, "Diwali sale", Guid.NewGuid(), versionId, null,
                [new CampaignAudienceMemberRequest("919876543210", null)], null, null),
            CancellationToken.None));
    }

    [Fact]
    public async Task A_template_version_that_is_not_APPROVED_is_rejected()
    {
        await using var db = InMemoryWaGatewayDbContext.Create(nameof(A_template_version_that_is_not_APPROVED_is_rejected));
        SeedPhoneNumber(db, TenantId, PhoneNumberId);
        var (_, versionId) = SeedApprovedTemplate(db, TenantId, versionStatus: "DRAFT");
        var handler = new CreateCampaignCommandHandler(db, Estimator().Object);

        await Assert.ThrowsAsync<ValidationException>(() => handler.HandleAsync(
            new CreateCampaignCommand(
                TenantId, "Diwali sale", PhoneNumberId, versionId, null,
                [new CampaignAudienceMemberRequest("919876543210", null)], null, null),
            CancellationToken.None));
    }

    [Fact]
    public async Task An_unknown_template_version_id_is_rejected()
    {
        await using var db = InMemoryWaGatewayDbContext.Create(nameof(An_unknown_template_version_id_is_rejected));
        SeedPhoneNumber(db, TenantId, PhoneNumberId);
        var handler = new CreateCampaignCommandHandler(db, Estimator().Object);

        await Assert.ThrowsAsync<ValidationException>(() => handler.HandleAsync(
            new CreateCampaignCommand(
                TenantId, "Diwali sale", PhoneNumberId, Guid.NewGuid(), null,
                [new CampaignAudienceMemberRequest("919876543210", null)], null, null),
            CancellationToken.None));
    }

    [Fact]
    public async Task Projected_cost_is_the_per_message_estimate_times_the_non_suppressed_audience()
    {
        await using var db = InMemoryWaGatewayDbContext.Create(nameof(Projected_cost_is_the_per_message_estimate_times_the_non_suppressed_audience));
        SeedPhoneNumber(db, TenantId, PhoneNumberId);
        var (_, versionId) = SeedApprovedTemplate(db, TenantId, category: "marketing");
        db.SuppressionListEntries.Add(new SuppressionListEntry
        {
            Id = Guid.NewGuid(), TenantId = TenantId, WaId = "919876543210", Reason = "stop_keyword",
            CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow,
        });
        await db.SaveChangesAsync(CancellationToken.None);
        var handler = new CreateCampaignCommandHandler(
            db, Estimator(new BillingEstimateResult(Found: true, Billable: true, Amount: 0.80m, Currency: "INR")).Object);

        var result = await handler.HandleAsync(
            new CreateCampaignCommand(
                TenantId, "Diwali sale", PhoneNumberId, versionId, null,
                [new CampaignAudienceMemberRequest("919876543210", null), new CampaignAudienceMemberRequest("919876543211", null),
                 new CampaignAudienceMemberRequest("919876543212", null)],
                null, null),
            CancellationToken.None);

        // 3 total - 1 suppressed = 2 billable recipients * 0.80 = 1.60
        Assert.Equal(1.60m, result.ProjectedCost);
        Assert.Equal("INR", result.ProjectedCurrency);
    }

    [Fact]
    public async Task Projected_cost_is_null_when_the_estimator_is_unreachable()
    {
        await using var db = InMemoryWaGatewayDbContext.Create(nameof(Projected_cost_is_null_when_the_estimator_is_unreachable));
        SeedPhoneNumber(db, TenantId, PhoneNumberId);
        var (_, versionId) = SeedApprovedTemplate(db, TenantId);
        var handler = new CreateCampaignCommandHandler(db, Estimator(result: null).Object);

        var result = await handler.HandleAsync(
            new CreateCampaignCommand(
                TenantId, "Diwali sale", PhoneNumberId, versionId, null,
                [new CampaignAudienceMemberRequest("919876543210", null)], null, null),
            CancellationToken.None);

        Assert.Null(result.ProjectedCost);
        Assert.Null(result.ProjectedCurrency);
        // Unreachable estimator must never block creation (advisory only, spec §4.3).
        Assert.Equal("draft", result.Status);
    }
}
