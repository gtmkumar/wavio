using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using WaAdmin.Application.Common.Interfaces;
using WaAdmin.Application.Templates;
using WaAdmin.Application.Templates.Commands.CreateTemplate;
using WaAdmin.Application.Templates.Dtos;
using WaAdmin.Infrastructure.Templates;
using WaPlatform.Contracts.TemplateDsl;
using WaPlatform.IntegrationTests.Support;
using Xunit;

namespace WaPlatform.IntegrationTests.Templates;

/// <summary>
/// Covers the regression QA proved has no automated net (PR #44, issue #16): reverting
/// <c>CreateTemplateCommandHandler</c>'s two-step save (SaveChanges -> set
/// <c>template.CurrentVersionId</c> -> SaveChanges again) back to one batch passed all 97
/// WaAdmin.Tests unit tests, because EF Core's InMemory provider does not enforce the genuinely
/// circular <c>Template.CurrentVersionId ↔ TemplateVersion.TemplateId</c> FK pair (see
/// .claude/agent-memory/qa-test-engineer/review-pr44-template-lifecycle.md and
/// db/migrations/V009__templates.sql's <c>templates_current_version_id_fkey</c>, added by a
/// second ALTER TABLE after both tables exist — real, not incidental). This test drives the REAL
/// <see cref="CreateTemplateCommandHandler"/> against real Postgres and FAILS if the two-step save
/// is ever reverted (proved live during this task — see the final report for the revert/restore
/// transcript).
/// </summary>
[Collection("IntegrationTests")]
public sealed class CreateTemplateCircularFkTests
{
    private readonly DatabaseFixture _fixture;

    public CreateTemplateCircularFkTests(DatabaseFixture fixture) => _fixture = fixture;

    [RequiresDockerFact]
    public async Task HandleAsync_ValidTemplate_TwoStepSaveSatisfiesCircularForeignKeyAgainstRealPostgres()
    {
        var tenantId = Guid.NewGuid();
        var businessAccountId = Guid.NewGuid();
        await SqlSeeding.SeedTenantAsync(_fixture.AdminConnectionString, tenantId, $"it-{tenantId:N}"[..18]);
        await SqlSeeding.SeedBusinessAccountAsync(_fixture.AdminConnectionString, businessAccountId, tenantId, $"waba-{businessAccountId:N}"[..18]);

        var (provider, currentTenant) = TestHost.BuildAdminProvider(_fixture.AppConnectionString);
        await using var disposableProvider = provider;
        currentTenant.TenantId = tenantId;

        await using var scope = provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<IWaAdminDbContext>();

        // The Meta Graph API is the one genuine external boundary here (same as the existing
        // CreateTemplateCommandHandlerTests unit test) — everything else (db, lint) is real.
        var graph = new Mock<IWhatsAppTemplateGraphClient>();
        graph.Setup(g => g.SubmitTemplateAsync(It.IsAny<GraphTemplateSubmitRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GraphTemplateSubmitResult(true, "meta-tpl-itest", null));

        var submission = new TemplateSubmissionService(
            db, graph.Object, [new StubTemplateLintService()], NullLogger<TemplateSubmissionService>.Instance);
        var handler = new CreateTemplateCommandHandler(db, submission);

        var definition = new TemplateDefinition
        {
            Name = "order_confirmation",
            Language = "en_US",
            Category = TemplateCategory.Utility,
            Components = [new TemplateComponent { Type = "BODY", Text = "Your order {{1}} has shipped." }],
        };
        var command = new CreateTemplateCommand(
            new CreateTemplateRequest(businessAccountId, definition, null), tenantId, Guid.NewGuid());

        var result = await handler.HandleAsync(command, CancellationToken.None);

        // If the two-step save were ever collapsed back into one batch, this call throws a real
        // Npgsql FK violation (23503) here — that IS the regression this test exists to catch.
        Assert.True(result.SubmittedToMeta);
        Assert.Null(result.SubmissionError);
        Assert.NotNull(result.Template.CurrentVersionId);
        Assert.NotNull(result.Template.CurrentVersion);
        Assert.Equal(1, result.Template.CurrentVersion!.VersionNumber);
        Assert.Equal(result.Template.CurrentVersionId, result.Template.CurrentVersion.Id);
    }
}
