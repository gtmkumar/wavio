using WaAdmin.Application.Common.Interfaces;
using WaAdmin.Application.Templates;
using WaAdmin.Application.Templates.Commands.CreateTemplate;
using WaAdmin.Application.Templates.Dtos;
using WaAdmin.Application.Templates.StateMachine;
using WaAdmin.Infrastructure.Templates;
using WaAdmin.Tests.Fakes;
using wavio.Utilities.Exceptions;
using WaPlatform.Contracts.TemplateDsl;
using Moq;
using Xunit;

namespace WaAdmin.Tests.Templates;

public class CreateTemplateCommandHandlerTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid BusinessAccountId = Guid.NewGuid();
    private const string MetaWabaId = "waba-meta-123";

    private static TemplateDefinition Definition(string name = "order_confirmation") => new()
    {
        Name = name,
        Language = "en_US",
        Category = TemplateCategory.Utility,
        Components = [new TemplateComponent { Type = "BODY", Text = "Your order {{1}} has shipped." }],
    };

    private static InMemoryWaAdminDbContext NewDb([System.Runtime.CompilerServices.CallerMemberName] string name = "")
    {
        var db = InMemoryWaAdminDbContext.Create(name);
        db.BusinessAccountMetaWabaIds[BusinessAccountId] = MetaWabaId;
        return db;
    }

    [Fact]
    public async Task HandleAsync_ValidTemplate_CreatesDraftAndSubmitsToMeta()
    {
        await using var db = NewDb();
        var graph = new Mock<IWhatsAppTemplateGraphClient>();
        graph.Setup(g => g.SubmitTemplateAsync(It.IsAny<GraphTemplateSubmitRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GraphTemplateSubmitResult(true, "meta-tpl-1", null));

        var submission = new TemplateSubmissionService(db, graph.Object, NullLogger());
        var handler = new CreateTemplateCommandHandler(db, new StubTemplateLintService(), submission);

        var command = new CreateTemplateCommand(
            new CreateTemplateRequest(BusinessAccountId, Definition(), null), TenantId, Guid.NewGuid());

        var result = await handler.HandleAsync(command, CancellationToken.None);

        Assert.True(result.SubmittedToMeta);
        Assert.Null(result.SubmissionError);
        Assert.Equal(TemplateStatusTransitions.Pending, result.Template.Status);
        Assert.Equal("meta-tpl-1", result.Template.MetaTemplateId);
        Assert.NotNull(result.Template.CurrentVersion);
        Assert.Equal(TemplateStatusTransitions.Pending, result.Template.CurrentVersion!.Status);

        // Every transition must be recorded (issue #16 acceptance criterion).
        var events = db.TemplateStatusEvents.ToList();
        var evt = Assert.Single(events);
        Assert.Equal(TemplateStatusTransitions.Draft, evt.OldStatus);
        Assert.Equal(TemplateStatusTransitions.Pending, evt.NewStatus);

        // Lint always runs and is recorded, even though the stub always passes.
        var lint = Assert.Single(db.TemplateLintResults.ToList());
        Assert.True(lint.Passed);
        Assert.Equal("stub", lint.Linter);
    }

    [Fact]
    public async Task HandleAsync_GraphRejectsSubmission_TemplateStaysDraftNotAnException()
    {
        await using var db = NewDb();
        var graph = new Mock<IWhatsAppTemplateGraphClient>();
        graph.Setup(g => g.SubmitTemplateAsync(It.IsAny<GraphTemplateSubmitRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GraphTemplateSubmitResult(false, null, "Template name violates policy."));

        var submission = new TemplateSubmissionService(db, graph.Object, NullLogger());
        var handler = new CreateTemplateCommandHandler(db, new StubTemplateLintService(), submission);

        var command = new CreateTemplateCommand(
            new CreateTemplateRequest(BusinessAccountId, Definition(), null), TenantId, Guid.NewGuid());

        var result = await handler.HandleAsync(command, CancellationToken.None);

        Assert.False(result.SubmittedToMeta);
        Assert.Equal("Template name violates policy.", result.SubmissionError);
        // The row is durable and stays DRAFT — the tenant can fix and resubmit, not lost work.
        Assert.Equal(TemplateStatusTransitions.Draft, result.Template.Status);
        Assert.Empty(db.TemplateStatusEvents.ToList());
    }

    [Fact]
    public async Task HandleAsync_GraphThrows_TemplateStaysDraftAndDoesNotPropagate()
    {
        await using var db = NewDb();
        var graph = new Mock<IWhatsAppTemplateGraphClient>();
        graph.Setup(g => g.SubmitTemplateAsync(It.IsAny<GraphTemplateSubmitRequest>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("connection refused"));

        var submission = new TemplateSubmissionService(db, graph.Object, NullLogger());
        var handler = new CreateTemplateCommandHandler(db, new StubTemplateLintService(), submission);

        var command = new CreateTemplateCommand(
            new CreateTemplateRequest(BusinessAccountId, Definition(), null), TenantId, Guid.NewGuid());

        var result = await handler.HandleAsync(command, CancellationToken.None);

        Assert.False(result.SubmittedToMeta);
        Assert.Contains("connection refused", result.SubmissionError);
        Assert.Equal(TemplateStatusTransitions.Draft, result.Template.Status);
    }

    [Fact]
    public async Task HandleAsync_LintFails_NeverCallsGraphClient()
    {
        await using var db = NewDb();
        var graph = new Mock<IWhatsAppTemplateGraphClient>();
        var failingLint = new Mock<ITemplateLintService>();
        failingLint.SetupGet(l => l.Linter).Returns("stub");
        failingLint.Setup(l => l.LintAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TemplateLintOutcome(false, "[{\"code\":\"banned_word\"}]", 10));

        var submission = new TemplateSubmissionService(db, graph.Object, NullLogger());
        var handler = new CreateTemplateCommandHandler(db, failingLint.Object, submission);

        var command = new CreateTemplateCommand(
            new CreateTemplateRequest(BusinessAccountId, Definition(), null), TenantId, Guid.NewGuid());

        var result = await handler.HandleAsync(command, CancellationToken.None);

        Assert.False(result.SubmittedToMeta);
        Assert.Equal(TemplateStatusTransitions.Draft, result.Template.Status);
        graph.Verify(g => g.SubmitTemplateAsync(It.IsAny<GraphTemplateSubmitRequest>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task HandleAsync_DuplicateNameLanguageForSameBusinessAccount_ThrowsBusinessRuleException()
    {
        await using var db = NewDb();
        var graph = new Mock<IWhatsAppTemplateGraphClient>();
        graph.Setup(g => g.SubmitTemplateAsync(It.IsAny<GraphTemplateSubmitRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GraphTemplateSubmitResult(true, "meta-tpl-1", null));

        var submission = new TemplateSubmissionService(db, graph.Object, NullLogger());
        var handler = new CreateTemplateCommandHandler(db, new StubTemplateLintService(), submission);

        var command = new CreateTemplateCommand(
            new CreateTemplateRequest(BusinessAccountId, Definition(), null), TenantId, Guid.NewGuid());
        await handler.HandleAsync(command, CancellationToken.None);

        await Assert.ThrowsAsync<BusinessRuleException>(() => handler.HandleAsync(command, CancellationToken.None));
    }

    private static Microsoft.Extensions.Logging.Abstractions.NullLogger<TemplateSubmissionService> NullLogger() =>
        Microsoft.Extensions.Logging.Abstractions.NullLogger<TemplateSubmissionService>.Instance;
}
