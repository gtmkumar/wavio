using WaAdmin.Application.Common.Interfaces;
using WaAdmin.Application.Templates;
using WaAdmin.Application.Templates.Commands.SubmitTemplate;
using WaAdmin.Application.Templates.StateMachine;
using wavio.SharedDataModel.Entities.Templates;
using WaAdmin.Tests.Fakes;
using Moq;
using Xunit;

namespace WaAdmin.Tests.Templates;

/// <summary>
/// Covers the gap issue #27 closed: before, only CreateTemplateCommandHandler's own inline lint
/// call gated submission — the standalone resubmit path (a template edited after rejection, then
/// resubmitted via POST /v1/templates/{id}/submit) skipped linting entirely. The gate now lives in
/// TemplateSubmissionService.SubmitAsync itself, so this path is covered identically to create.
/// </summary>
public class SubmitTemplateCommandHandlerTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid BusinessAccountId = Guid.NewGuid();
    private const string MetaWabaId = "waba-meta-123";

    private static InMemoryWaAdminDbContext NewDb([System.Runtime.CompilerServices.CallerMemberName] string name = "")
    {
        var db = InMemoryWaAdminDbContext.Create(name);
        db.BusinessAccountMetaWabaIds[BusinessAccountId] = MetaWabaId;
        return db;
    }

    private static async Task<(Template Template, TemplateVersion Version)> SeedDraftTemplateAsync(InMemoryWaAdminDbContext db)
    {
        var now = DateTimeOffset.UtcNow;
        var template = new Template
        {
            Id = Guid.NewGuid(),
            TenantId = TenantId,
            BusinessAccountId = BusinessAccountId,
            Name = "reminder",
            Language = "en_US",
            Category = "utility",
            Status = TemplateStatusTransitions.Draft,
            CreatedAt = now,
            UpdatedAt = now,
            Version = 1,
        };
        var version = new TemplateVersion
        {
            Id = Guid.NewGuid(),
            TenantId = TenantId,
            TemplateId = template.Id,
            VersionNumber = 1,
            Components = """[{"type":"BODY","text":"Your appointment is confirmed."}]""",
            Status = TemplateStatusTransitions.Draft,
            CreatedAt = now,
            UpdatedAt = now,
        };
        db.Templates.Add(template);
        db.TemplateVersions.Add(version);
        await db.SaveChangesAsync(CancellationToken.None);
        template.CurrentVersionId = version.Id;
        await db.SaveChangesAsync(CancellationToken.None);
        return (template, version);
    }

    [Fact]
    public async Task HandleAsync_LintFails_NeverCallsGraphClient_AndRecordsLintResult()
    {
        await using var db = NewDb();
        var (template, _) = await SeedDraftTemplateAsync(db);

        var graph = new Mock<IWhatsAppTemplateGraphClient>();
        var failingLint = new Mock<ITemplateLintService>();
        failingLint.SetupGet(l => l.Linter).Returns("rules");
        failingLint.Setup(l => l.LintAsync(It.IsAny<TemplateLintInput>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TemplateLintOutcome(false, "[{\"code\":\"UTILITY_PROMOTIONAL_LANGUAGE\"}]", 60));

        var submission = new TemplateSubmissionService(
            db, graph.Object, [failingLint.Object],
            Microsoft.Extensions.Logging.Abstractions.NullLogger<TemplateSubmissionService>.Instance);
        var handler = new SubmitTemplateCommandHandler(db, submission);

        var result = await handler.HandleAsync(new SubmitTemplateCommand(template.Id, TenantId, null), CancellationToken.None);

        Assert.False(result.SubmittedToMeta);
        Assert.Contains("Lint failed", result.SubmissionError);
        Assert.Equal(TemplateStatusTransitions.Draft, result.Template.Status);
        graph.Verify(g => g.SubmitTemplateAsync(It.IsAny<GraphTemplateSubmitRequest>(), It.IsAny<CancellationToken>()), Times.Never);

        var lint = Assert.Single(db.TemplateLintResults.ToList());
        Assert.False(lint.Passed);
        Assert.Equal("rules", lint.Linter);
    }

    [Fact]
    public async Task HandleAsync_AllLintersPass_SubmitsToMeta()
    {
        await using var db = NewDb();
        var (template, _) = await SeedDraftTemplateAsync(db);

        var graph = new Mock<IWhatsAppTemplateGraphClient>();
        graph.Setup(g => g.SubmitTemplateAsync(It.IsAny<GraphTemplateSubmitRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GraphTemplateSubmitResult(true, "meta-tpl-2", null));

        var passingLint = new Mock<ITemplateLintService>();
        passingLint.SetupGet(l => l.Linter).Returns("rules");
        passingLint.Setup(l => l.LintAsync(It.IsAny<TemplateLintInput>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TemplateLintOutcome(true, "[]", 100));

        var submission = new TemplateSubmissionService(
            db, graph.Object, [passingLint.Object],
            Microsoft.Extensions.Logging.Abstractions.NullLogger<TemplateSubmissionService>.Instance);
        var handler = new SubmitTemplateCommandHandler(db, submission);

        var result = await handler.HandleAsync(new SubmitTemplateCommand(template.Id, TenantId, null), CancellationToken.None);

        Assert.True(result.SubmittedToMeta);
        Assert.Equal(TemplateStatusTransitions.Pending, result.Template.Status);
        var lint = Assert.Single(db.TemplateLintResults.ToList());
        Assert.True(lint.Passed);
    }

    [Fact]
    public async Task HandleAsync_MultipleLinters_RecordsOneRowPerLinter()
    {
        await using var db = NewDb();
        var (template, _) = await SeedDraftTemplateAsync(db);

        var graph = new Mock<IWhatsAppTemplateGraphClient>();
        graph.Setup(g => g.SubmitTemplateAsync(It.IsAny<GraphTemplateSubmitRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GraphTemplateSubmitResult(true, "meta-tpl-3", null));

        var rules = new Mock<ITemplateLintService>();
        rules.SetupGet(l => l.Linter).Returns("rules");
        rules.Setup(l => l.LintAsync(It.IsAny<TemplateLintInput>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TemplateLintOutcome(true, "[]", 100));

        var llm = new Mock<ITemplateLintService>();
        llm.SetupGet(l => l.Linter).Returns("llm");
        llm.Setup(l => l.LintAsync(It.IsAny<TemplateLintInput>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TemplateLintOutcome(true, "[]", 95));

        var submission = new TemplateSubmissionService(
            db, graph.Object, [rules.Object, llm.Object],
            Microsoft.Extensions.Logging.Abstractions.NullLogger<TemplateSubmissionService>.Instance);
        var handler = new SubmitTemplateCommandHandler(db, submission);

        await handler.HandleAsync(new SubmitTemplateCommand(template.Id, TenantId, null), CancellationToken.None);

        var linters = db.TemplateLintResults.Select(l => l.Linter).OrderBy(l => l).ToList();
        Assert.Equal(["llm", "rules"], linters);
    }
}
