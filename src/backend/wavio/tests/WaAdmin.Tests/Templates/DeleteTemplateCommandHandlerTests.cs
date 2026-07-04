using WaAdmin.Application.Templates.Commands.DeleteTemplate;
using WaAdmin.Application.Templates.StateMachine;
using WaAdmin.Tests.Fakes;
using wavio.SharedDataModel.Entities.Templates;
using wavio.Utilities.Exceptions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace WaAdmin.Tests.Templates;

public class DeleteTemplateCommandHandlerTests
{
    private static readonly Guid TenantId = Guid.NewGuid();

    private static Template Seed(InMemoryWaAdminDbContext db, string status)
    {
        var now = DateTimeOffset.UtcNow;
        var template = new Template
        {
            Id = Guid.NewGuid(), TenantId = TenantId, BusinessAccountId = Guid.NewGuid(),
            Name = "order_confirmation", Language = "en_US", Category = "utility", Status = status,
            CreatedAt = now, UpdatedAt = now, Version = 1,
        };
        db.Templates.Add(template);
        db.SaveChanges();
        return template;
    }

    [Fact]
    public async Task HandleAsync_DraftTemplate_SoftDeletes()
    {
        await using var db = InMemoryWaAdminDbContext.Create(nameof(HandleAsync_DraftTemplate_SoftDeletes));
        var template = Seed(db, TemplateStatusTransitions.Draft);
        var handler = new DeleteTemplateCommandHandler(db);

        var result = await handler.HandleAsync(new DeleteTemplateCommand(template.Id, TenantId, Guid.NewGuid()), CancellationToken.None);

        Assert.True(result);
        Assert.NotNull(db.Templates.IgnoreQueryFilters().Single(t => t.Id == template.Id).DeletedAt);
    }

    [Theory]
    [InlineData(TemplateStatusTransitions.Pending)]
    [InlineData(TemplateStatusTransitions.Approved)]
    public async Task HandleAsync_NonDraftTemplate_ThrowsBusinessRuleException(string status)
    {
        await using var db = InMemoryWaAdminDbContext.Create($"{nameof(HandleAsync_NonDraftTemplate_ThrowsBusinessRuleException)}_{status}");
        var template = Seed(db, status);
        var handler = new DeleteTemplateCommandHandler(db);

        await Assert.ThrowsAsync<BusinessRuleException>(
            () => handler.HandleAsync(new DeleteTemplateCommand(template.Id, TenantId, Guid.NewGuid()), CancellationToken.None));
    }

    [Fact]
    public async Task HandleAsync_UnknownTemplate_ReturnsFalse()
    {
        await using var db = InMemoryWaAdminDbContext.Create(nameof(HandleAsync_UnknownTemplate_ReturnsFalse));
        var handler = new DeleteTemplateCommandHandler(db);

        var result = await handler.HandleAsync(new DeleteTemplateCommand(Guid.NewGuid(), TenantId, Guid.NewGuid()), CancellationToken.None);

        Assert.False(result);
    }
}
