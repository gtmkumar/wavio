using WaAdmin.Application.Templates.Commands.UpdateTemplate;
using WaAdmin.Application.Templates.Dtos;
using WaAdmin.Application.Templates.StateMachine;
using WaAdmin.Tests.Fakes;
using wavio.SharedDataModel.Entities.Templates;
using wavio.Utilities.Exceptions;
using WaPlatform.Contracts.TemplateDsl;
using Xunit;

namespace WaAdmin.Tests.Templates;

public class UpdateTemplateCommandHandlerTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid BusinessAccountId = Guid.NewGuid();

    private static TemplateDefinition Definition(string name, string text) => new()
    {
        Name = name,
        Language = "en_US",
        Category = TemplateCategory.Utility,
        Components = [new TemplateComponent { Type = "BODY", Text = text }],
    };

    private static (Template Template, TemplateVersion Version) Seed(
        InMemoryWaAdminDbContext db, string templateStatus, string versionStatus, string name = "order_confirmation")
    {
        var now = DateTimeOffset.UtcNow;
        var template = new Template
        {
            Id = Guid.NewGuid(), TenantId = TenantId, BusinessAccountId = BusinessAccountId,
            Name = name, Language = "en_US", Category = "utility", Status = templateStatus,
            CreatedAt = now, UpdatedAt = now, Version = 1,
        };
        var version = new TemplateVersion
        {
            Id = Guid.NewGuid(), TenantId = TenantId, TemplateId = template.Id, VersionNumber = 1,
            Components = "[{\"type\":\"BODY\",\"text\":\"old text\"}]", Status = versionStatus,
            CreatedAt = now, UpdatedAt = now,
        };
        template.CurrentVersionId = version.Id;
        db.Templates.Add(template);
        db.TemplateVersions.Add(version);
        db.SaveChanges();
        return (template, version);
    }

    [Fact]
    public async Task HandleAsync_DraftVersion_EditsInPlaceWithoutNewVersionRow()
    {
        await using var db = InMemoryWaAdminDbContext.Create(nameof(HandleAsync_DraftVersion_EditsInPlaceWithoutNewVersionRow));
        var (template, version) = Seed(db, TemplateStatusTransitions.Draft, TemplateStatusTransitions.Draft);

        var handler = new UpdateTemplateCommandHandler(db);
        var command = new UpdateTemplateCommand(
            template.Id, new UpdateTemplateRequest(Definition(template.Name, "new text"), null), TenantId, Guid.NewGuid());

        var result = await handler.HandleAsync(command, CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal(TemplateStatusTransitions.Draft, result!.Status);
        Assert.Equal(1, db.TemplateVersions.Count()); // no new version created
        Assert.Contains("new text", result.CurrentVersion!.ComponentsJson);
        Assert.Empty(db.TemplateStatusEvents.ToList()); // in-place edit is not a status transition
    }

    [Fact]
    public async Task HandleAsync_ApprovedVersion_CreatesNewDraftVersionAndTemplateReturnsToDraft()
    {
        await using var db = InMemoryWaAdminDbContext.Create(nameof(HandleAsync_ApprovedVersion_CreatesNewDraftVersionAndTemplateReturnsToDraft));
        var (template, approvedVersion) = Seed(db, TemplateStatusTransitions.Approved, TemplateStatusTransitions.Approved);

        var handler = new UpdateTemplateCommandHandler(db);
        var command = new UpdateTemplateCommand(
            template.Id, new UpdateTemplateRequest(Definition(template.Name, "revised text"), null), TenantId, Guid.NewGuid());

        var result = await handler.HandleAsync(command, CancellationToken.None);

        Assert.NotNull(result);
        // Immutability (issue #16 Task 6): the approved version's content is untouched.
        var reloadedApproved = db.TemplateVersions.Single(v => v.Id == approvedVersion.Id);
        Assert.DoesNotContain("revised text", reloadedApproved.Components);
        Assert.Equal(TemplateStatusTransitions.Approved, reloadedApproved.Status);

        // A new DRAFT version was created instead, and it — not the approved one — is current.
        Assert.Equal(2, db.TemplateVersions.Count());
        Assert.NotEqual(approvedVersion.Id, result!.CurrentVersionId);
        Assert.Equal(TemplateStatusTransitions.Draft, result.CurrentVersion!.Status);
        Assert.Equal(2, result.CurrentVersion.VersionNumber);
        Assert.Equal(TemplateStatusTransitions.Draft, result.Status);

        var evt = Assert.Single(db.TemplateStatusEvents.ToList());
        Assert.Equal(TemplateStatusTransitions.Approved, evt.OldStatus);
        Assert.Equal(TemplateStatusTransitions.Draft, evt.NewStatus);
    }

    [Theory]
    [InlineData(TemplateStatusTransitions.Pending)]
    [InlineData(TemplateStatusTransitions.Disabled)]
    public async Task HandleAsync_MidReviewOrTerminal_ThrowsBusinessRuleException(string status)
    {
        await using var db = InMemoryWaAdminDbContext.Create($"{nameof(HandleAsync_MidReviewOrTerminal_ThrowsBusinessRuleException)}_{status}");
        var (template, _) = Seed(db, status, status);

        var handler = new UpdateTemplateCommandHandler(db);
        var command = new UpdateTemplateCommand(
            template.Id, new UpdateTemplateRequest(Definition(template.Name, "x"), null), TenantId, Guid.NewGuid());

        await Assert.ThrowsAsync<BusinessRuleException>(() => handler.HandleAsync(command, CancellationToken.None));
    }

    [Fact]
    public async Task HandleAsync_RenamingTemplate_ThrowsValidationException()
    {
        await using var db = InMemoryWaAdminDbContext.Create(nameof(HandleAsync_RenamingTemplate_ThrowsValidationException));
        var (template, _) = Seed(db, TemplateStatusTransitions.Draft, TemplateStatusTransitions.Draft);

        var handler = new UpdateTemplateCommandHandler(db);
        var command = new UpdateTemplateCommand(
            template.Id, new UpdateTemplateRequest(Definition("a_completely_different_name", "x"), null), TenantId, Guid.NewGuid());

        await Assert.ThrowsAsync<ValidationException>(() => handler.HandleAsync(command, CancellationToken.None));
    }

    [Fact]
    public async Task HandleAsync_UnknownTemplate_ReturnsNull()
    {
        await using var db = InMemoryWaAdminDbContext.Create(nameof(HandleAsync_UnknownTemplate_ReturnsNull));
        var handler = new UpdateTemplateCommandHandler(db);
        var command = new UpdateTemplateCommand(
            Guid.NewGuid(), new UpdateTemplateRequest(Definition("x", "x"), null), TenantId, Guid.NewGuid());

        var result = await handler.HandleAsync(command, CancellationToken.None);

        Assert.Null(result);
    }
}
