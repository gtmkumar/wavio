using WaAdmin.Application.Common.Interfaces;
using WaAdmin.Application.Templates.Commands.ProcessTemplateStatusChanged;
using WaAdmin.Application.Templates.StateMachine;
using WaAdmin.Tests.Fakes;
using wavio.SharedDataModel.Entities.Templates;
using WaPlatform.Contracts.IntegrationEvents.V1;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace WaAdmin.Tests.Templates;

public class ProcessTemplateStatusChangedCommandHandlerTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private const string MetaTemplateId = "meta-tpl-1";

    private static Template Seed(InMemoryWaAdminDbContext db, string status, short pauseCount = 0)
    {
        var now = DateTimeOffset.UtcNow;
        var template = new Template
        {
            Id = Guid.NewGuid(), TenantId = TenantId, BusinessAccountId = Guid.NewGuid(),
            Name = "order_confirmation", Language = "en_US", Category = "utility",
            MetaTemplateId = MetaTemplateId, Status = status, PauseCount = pauseCount,
            CreatedAt = now, UpdatedAt = now, Version = 1,
        };
        db.Templates.Add(template);
        db.SaveChanges();
        return template;
    }

    private static TemplateStatusChangedV1 Event(Guid tenantId, string previous, string next, string? reason = null) => new()
    {
        TenantId = tenantId,
        TemplateId = Guid.NewGuid(),
        MetaTemplateId = MetaTemplateId,
        PreviousStatus = previous,
        NewStatus = next,
        Reason = reason,
    };

    [Fact]
    public async Task HandleAsync_UnresolvableTenant_ParksAndDoesNotTouchDb()
    {
        await using var db = InMemoryWaAdminDbContext.Create(nameof(HandleAsync_UnresolvableTenant_ParksAndDoesNotTouchDb));
        Seed(db, TemplateStatusTransitions.Pending);
        var handler = new ProcessTemplateStatusChangedCommandHandler(
            db, Mock.Of<ICampaignFreezeHook>(), NullLogger<ProcessTemplateStatusChangedCommandHandler>.Instance);

        var result = await handler.HandleAsync(
            new ProcessTemplateStatusChangedCommand(Event(Guid.Empty, "PENDING", "APPROVED")), CancellationToken.None);

        Assert.False(result);
        Assert.Empty(db.TemplateStatusEvents.ToList());
    }

    [Fact]
    public async Task HandleAsync_UnknownTemplate_Parks()
    {
        await using var db = InMemoryWaAdminDbContext.Create(nameof(HandleAsync_UnknownTemplate_Parks));
        var handler = new ProcessTemplateStatusChangedCommandHandler(
            db, Mock.Of<ICampaignFreezeHook>(), NullLogger<ProcessTemplateStatusChangedCommandHandler>.Instance);

        var result = await handler.HandleAsync(
            new ProcessTemplateStatusChangedCommand(Event(TenantId, "PENDING", "APPROVED")), CancellationToken.None);

        Assert.False(result);
    }

    [Fact]
    public async Task HandleAsync_InvalidTransition_ParksAndDoesNotMutateTemplate()
    {
        await using var db = InMemoryWaAdminDbContext.Create(nameof(HandleAsync_InvalidTransition_ParksAndDoesNotMutateTemplate));
        var template = Seed(db, TemplateStatusTransitions.Draft); // DRAFT can only go to PENDING locally
        var handler = new ProcessTemplateStatusChangedCommandHandler(
            db, Mock.Of<ICampaignFreezeHook>(), NullLogger<ProcessTemplateStatusChangedCommandHandler>.Instance);

        var result = await handler.HandleAsync(
            new ProcessTemplateStatusChangedCommand(Event(TenantId, "DRAFT", "APPROVED")), CancellationToken.None);

        Assert.False(result);
        Assert.Equal(TemplateStatusTransitions.Draft, db.Templates.Single(t => t.Id == template.Id).Status);
        Assert.Empty(db.TemplateStatusEvents.ToList());
    }

    [Fact]
    public async Task HandleAsync_PendingToApproved_AppliesAndRecordsEvent()
    {
        await using var db = InMemoryWaAdminDbContext.Create(nameof(HandleAsync_PendingToApproved_AppliesAndRecordsEvent));
        var template = Seed(db, TemplateStatusTransitions.Pending);
        var handler = new ProcessTemplateStatusChangedCommandHandler(
            db, Mock.Of<ICampaignFreezeHook>(), NullLogger<ProcessTemplateStatusChangedCommandHandler>.Instance);

        var result = await handler.HandleAsync(
            new ProcessTemplateStatusChangedCommand(Event(TenantId, "PENDING", "APPROVED")), CancellationToken.None);

        Assert.True(result);
        Assert.Equal(TemplateStatusTransitions.Approved, db.Templates.Single(t => t.Id == template.Id).Status);
        var evt = Assert.Single(db.TemplateStatusEvents.ToList());
        Assert.Equal("PENDING", evt.OldStatus);
        Assert.Equal("APPROVED", evt.NewStatus);
    }

    [Fact]
    public async Task HandleAsync_FirstPause_SetsThreeHourWindowAndFreezesCampaigns()
    {
        await using var db = InMemoryWaAdminDbContext.Create(nameof(HandleAsync_FirstPause_SetsThreeHourWindowAndFreezesCampaigns));
        var template = Seed(db, TemplateStatusTransitions.Approved);
        var freeze = new Mock<ICampaignFreezeHook>();
        var handler = new ProcessTemplateStatusChangedCommandHandler(
            db, freeze.Object, NullLogger<ProcessTemplateStatusChangedCommandHandler>.Instance);

        var before = DateTimeOffset.UtcNow;
        var result = await handler.HandleAsync(
            new ProcessTemplateStatusChangedCommand(Event(TenantId, "APPROVED", "PAUSED", "quality")), CancellationToken.None);

        Assert.True(result);
        var reloaded = db.Templates.Single(t => t.Id == template.Id);
        Assert.Equal(TemplateStatusTransitions.Paused, reloaded.Status);
        Assert.Equal((short)1, reloaded.PauseCount);
        Assert.NotNull(reloaded.PausedUntil);
        Assert.InRange(reloaded.PausedUntil!.Value, before.AddHours(3), before.AddHours(3).AddMinutes(1));
        freeze.Verify(f => f.FreezeCampaignsUsingTemplateAsync(template.Id, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task HandleAsync_SecondPause_EscalatesToSixHourWindow()
    {
        await using var db = InMemoryWaAdminDbContext.Create(nameof(HandleAsync_SecondPause_EscalatesToSixHourWindow));
        var template = Seed(db, TemplateStatusTransitions.Approved, pauseCount: 1); // already paused once
        var handler = new ProcessTemplateStatusChangedCommandHandler(
            db, Mock.Of<ICampaignFreezeHook>(), NullLogger<ProcessTemplateStatusChangedCommandHandler>.Instance);

        var before = DateTimeOffset.UtcNow;
        await handler.HandleAsync(
            new ProcessTemplateStatusChangedCommand(Event(TenantId, "APPROVED", "PAUSED", "quality")), CancellationToken.None);

        var reloaded = db.Templates.Single(t => t.Id == template.Id);
        Assert.Equal((short)2, reloaded.PauseCount);
        Assert.InRange(reloaded.PausedUntil!.Value, before.AddHours(6), before.AddHours(6).AddMinutes(1));
    }

    [Fact]
    public async Task HandleAsync_Disabled_ClearsPauseWindowAndFreezesCampaigns()
    {
        await using var db = InMemoryWaAdminDbContext.Create(nameof(HandleAsync_Disabled_ClearsPauseWindowAndFreezesCampaigns));
        var template = Seed(db, TemplateStatusTransitions.Paused, pauseCount: 2);
        var freeze = new Mock<ICampaignFreezeHook>();
        var handler = new ProcessTemplateStatusChangedCommandHandler(
            db, freeze.Object, NullLogger<ProcessTemplateStatusChangedCommandHandler>.Instance);

        var result = await handler.HandleAsync(
            new ProcessTemplateStatusChangedCommand(Event(TenantId, "PAUSED", "DISABLED", "repeated quality failures")),
            CancellationToken.None);

        Assert.True(result);
        var reloaded = db.Templates.Single(t => t.Id == template.Id);
        Assert.Equal(TemplateStatusTransitions.Disabled, reloaded.Status);
        Assert.Null(reloaded.PausedUntil);
        freeze.Verify(f => f.FreezeCampaignsUsingTemplateAsync(template.Id, It.IsAny<CancellationToken>()), Times.Once);
    }
}
