using WaAdmin.Application.Common.Interfaces;
using WaAdmin.Application.Templates.Commands.ProcessTemplateCategoryChanged;
using WaAdmin.Application.Templates.StateMachine;
using WaAdmin.Tests.Fakes;
using wavio.SharedDataModel.Entities.Templates;
using WaPlatform.Contracts.IntegrationEvents.V1;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace WaAdmin.Tests.Templates;

public class ProcessTemplateCategoryChangedCommandHandlerTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private const string MetaTemplateId = "meta-tpl-1";

    private static Template Seed(InMemoryWaAdminDbContext db, string category = "utility")
    {
        var now = DateTimeOffset.UtcNow;
        var template = new Template
        {
            Id = Guid.NewGuid(), TenantId = TenantId, BusinessAccountId = Guid.NewGuid(),
            Name = "order_confirmation", Language = "en_US", Category = category,
            MetaTemplateId = MetaTemplateId, Status = TemplateStatusTransitions.Approved,
            CreatedAt = now, UpdatedAt = now, Version = 1,
        };
        db.Templates.Add(template);
        db.SaveChanges();
        return template;
    }

    private static TemplateCategoryChangedV1 Event(Guid tenantId, string previous, string next) => new()
    {
        TenantId = tenantId,
        TemplateId = Guid.NewGuid(),
        MetaTemplateId = MetaTemplateId,
        PreviousCategory = previous,
        NewCategory = next,
    };

    [Fact]
    public async Task HandleAsync_UnresolvableTenant_Parks()
    {
        await using var db = InMemoryWaAdminDbContext.Create(nameof(HandleAsync_UnresolvableTenant_Parks));
        Seed(db);
        var handler = new ProcessTemplateCategoryChangedCommandHandler(
            db, Mock.Of<ITenantAlertPublisher>(), Mock.Of<IBillingRecalibrationHook>(),
            NullLogger<ProcessTemplateCategoryChangedCommandHandler>.Instance);

        var result = await handler.HandleAsync(
            new ProcessTemplateCategoryChangedCommand(Event(Guid.Empty, "utility", "marketing")), CancellationToken.None);

        Assert.False(result);
        Assert.Empty(db.TemplateCategoryChanges.ToList());
    }

    [Fact]
    public async Task HandleAsync_UnknownTemplate_Parks()
    {
        await using var db = InMemoryWaAdminDbContext.Create(nameof(HandleAsync_UnknownTemplate_Parks));
        var handler = new ProcessTemplateCategoryChangedCommandHandler(
            db, Mock.Of<ITenantAlertPublisher>(), Mock.Of<IBillingRecalibrationHook>(),
            NullLogger<ProcessTemplateCategoryChangedCommandHandler>.Instance);

        var result = await handler.HandleAsync(
            new ProcessTemplateCategoryChangedCommand(Event(TenantId, "utility", "marketing")), CancellationToken.None);

        Assert.False(result);
    }

    [Fact]
    public async Task HandleAsync_ValidChange_UpdatesCategoryRecordsChangeAlertsAndRecalibrates()
    {
        await using var db = InMemoryWaAdminDbContext.Create(nameof(HandleAsync_ValidChange_UpdatesCategoryRecordsChangeAlertsAndRecalibrates));
        var template = Seed(db, category: "utility");

        var alerts = new Mock<ITenantAlertPublisher>();
        var billing = new Mock<IBillingRecalibrationHook>();
        var handler = new ProcessTemplateCategoryChangedCommandHandler(
            db, alerts.Object, billing.Object, NullLogger<ProcessTemplateCategoryChangedCommandHandler>.Instance);

        var result = await handler.HandleAsync(
            new ProcessTemplateCategoryChangedCommand(Event(TenantId, "utility", "marketing")), CancellationToken.None);

        Assert.True(result);
        Assert.Equal("marketing", db.Templates.Single(t => t.Id == template.Id).Category);

        var change = Assert.Single(db.TemplateCategoryChanges.ToList());
        Assert.Equal("utility", change.OldCategory);
        Assert.Equal("marketing", change.NewCategory);
        Assert.NotNull(change.TenantAlertedAt);
        Assert.NotNull(change.BillingRecalibratedAt);

        alerts.Verify(a => a.RaiseAsync(TenantId, "template.category_changed", It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
        billing.Verify(b => b.RecalibrateAsync(template.Id, "utility", "marketing", It.IsAny<CancellationToken>()), Times.Once);
    }
}
