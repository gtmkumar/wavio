using WaAdmin.Application.Templates.Queries.GetTemplateApprovalMetrics;
using WaAdmin.Application.Templates.StateMachine;
using wavio.SharedDataModel.Entities.Templates;
using WaAdmin.Tests.Fakes;
using Xunit;

namespace WaAdmin.Tests.Templates;

public class GetTemplateApprovalMetricsQueryHandlerTests
{
    private static readonly Guid TenantId = Guid.NewGuid();

    private static TemplateStatusEvent ReviewEvent(string newStatus, Guid? versionId = null) => new()
    {
        Id = Guid.NewGuid(),
        TenantId = TenantId,
        TemplateId = Guid.NewGuid(),
        TemplateVersionId = versionId ?? Guid.NewGuid(),
        OldStatus = TemplateStatusTransitions.Pending,
        NewStatus = newStatus,
        OccurredAt = DateTimeOffset.UtcNow,
        CreatedAt = DateTimeOffset.UtcNow,
    };

    private static TemplateLintResult LintRow(bool passed) => new()
    {
        Id = Guid.NewGuid(),
        TenantId = TenantId,
        TemplateVersionId = Guid.NewGuid(),
        Linter = "rules",
        Passed = passed,
        Findings = "[]",
        CreatedAt = DateTimeOffset.UtcNow,
    };

    [Fact]
    public async Task HandleAsync_NoData_ReturnsZeroCountsAndNullRates()
    {
        await using var db = InMemoryWaAdminDbContext.Create(nameof(HandleAsync_NoData_ReturnsZeroCountsAndNullRates));
        var handler = new GetTemplateApprovalMetricsQueryHandler(db);

        var result = await handler.HandleAsync(new GetTemplateApprovalMetricsQuery(), CancellationToken.None);

        Assert.Equal(0, result.ReviewedVersionCount);
        Assert.Equal(0, result.FirstPassApprovedCount);
        Assert.Null(result.FirstPassApprovalRate);
        Assert.Equal(0, result.LintRunCount);
        Assert.Equal(0, result.LintPassedCount);
        Assert.Null(result.LintPassRate);
    }

    [Fact]
    public async Task HandleAsync_MixOfApprovedAndRejected_ComputesFirstPassApprovalRate()
    {
        await using var db = InMemoryWaAdminDbContext.Create(nameof(HandleAsync_MixOfApprovedAndRejected_ComputesFirstPassApprovalRate));
        db.TemplateStatusEvents.AddRange(
            ReviewEvent(TemplateStatusTransitions.Approved),
            ReviewEvent(TemplateStatusTransitions.Approved),
            ReviewEvent(TemplateStatusTransitions.Approved),
            ReviewEvent(TemplateStatusTransitions.Rejected));
        await db.SaveChangesAsync(CancellationToken.None);

        var handler = new GetTemplateApprovalMetricsQueryHandler(db);
        var result = await handler.HandleAsync(new GetTemplateApprovalMetricsQuery(), CancellationToken.None);

        Assert.Equal(4, result.ReviewedVersionCount);
        Assert.Equal(3, result.FirstPassApprovedCount);
        Assert.Equal(0.75, result.FirstPassApprovalRate);
    }

    [Fact]
    public async Task HandleAsync_IgnoresNonReviewTransitions()
    {
        await using var db = InMemoryWaAdminDbContext.Create(nameof(HandleAsync_IgnoresNonReviewTransitions));
        var approvedVersionId = Guid.NewGuid();
        db.TemplateStatusEvents.AddRange(
            ReviewEvent(TemplateStatusTransitions.Approved, approvedVersionId),
            // A later pause/unpause on the SAME version must not be double-counted as another
            // review decision — OldStatus here is PAUSED/APPROVED, not PENDING.
            new TemplateStatusEvent
            {
                Id = Guid.NewGuid(), TenantId = TenantId, TemplateId = Guid.NewGuid(),
                TemplateVersionId = approvedVersionId, OldStatus = TemplateStatusTransitions.Approved,
                NewStatus = TemplateStatusTransitions.Paused, OccurredAt = DateTimeOffset.UtcNow,
                CreatedAt = DateTimeOffset.UtcNow,
            });
        await db.SaveChangesAsync(CancellationToken.None);

        var handler = new GetTemplateApprovalMetricsQueryHandler(db);
        var result = await handler.HandleAsync(new GetTemplateApprovalMetricsQuery(), CancellationToken.None);

        Assert.Equal(1, result.ReviewedVersionCount);
        Assert.Equal(1, result.FirstPassApprovedCount);
        Assert.Equal(1.0, result.FirstPassApprovalRate);
    }

    [Fact]
    public async Task HandleAsync_LintRows_ComputesPassRate()
    {
        await using var db = InMemoryWaAdminDbContext.Create(nameof(HandleAsync_LintRows_ComputesPassRate));
        db.TemplateLintResults.AddRange(LintRow(true), LintRow(true), LintRow(false), LintRow(true));
        await db.SaveChangesAsync(CancellationToken.None);

        var handler = new GetTemplateApprovalMetricsQueryHandler(db);
        var result = await handler.HandleAsync(new GetTemplateApprovalMetricsQuery(), CancellationToken.None);

        Assert.Equal(4, result.LintRunCount);
        Assert.Equal(3, result.LintPassedCount);
        Assert.Equal(0.75, result.LintPassRate);
    }
}
