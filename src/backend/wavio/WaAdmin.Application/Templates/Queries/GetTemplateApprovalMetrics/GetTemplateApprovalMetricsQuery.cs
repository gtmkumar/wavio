using WaAdmin.Application.Common.Interfaces;
using WaAdmin.Application.Templates.Dtos;
using WaAdmin.Application.Templates.StateMachine;
using Wavio.Utilities.CQRS.Abstractions;
using Microsoft.EntityFrameworkCore;

namespace WaAdmin.Application.Templates.Queries.GetTemplateApprovalMetrics;

/// <summary>
/// GET /v1/templates/metrics/approval-rate (issue #27, spec §4.4: "Target: &gt;90% first-pass
/// approval rate"). Tenant scoping comes from RLS (app.tenant_id) on both source tables, not an
/// explicit filter here — same convention as GetTemplatesQueryHandler.
/// </summary>
public sealed record GetTemplateApprovalMetricsQuery : IQuery<TemplateApprovalMetricsDto>;

public sealed class GetTemplateApprovalMetricsQueryHandler
    : IQueryHandler<GetTemplateApprovalMetricsQuery, TemplateApprovalMetricsDto>
{
    private readonly IWaAdminDbContext _db;
    public GetTemplateApprovalMetricsQueryHandler(IWaAdminDbContext db) => _db = db;

    public async Task<TemplateApprovalMetricsDto> HandleAsync(
        GetTemplateApprovalMetricsQuery query, CancellationToken cancellationToken)
    {
        // Each template_versions row is immutable post-submission (issue #16) and can only ever
        // undergo ONE PENDING -> {APPROVED|REJECTED} transition — a REJECTED version is retired,
        // never resubmitted (UpdateTemplateCommandHandler creates a brand new version instead).
        // So "PENDING -> APPROVED with no prior REJECTED for that version" (the issue's own
        // definition) collapses to: did that one review transition land on APPROVED or REJECTED.
        var reviewDecisions = await _db.TemplateStatusEvents.AsNoTracking()
            .Where(e => e.OldStatus == TemplateStatusTransitions.Pending
                     && (e.NewStatus == TemplateStatusTransitions.Approved
                      || e.NewStatus == TemplateStatusTransitions.Rejected))
            .Select(e => e.NewStatus)
            .ToListAsync(cancellationToken);

        var reviewed = reviewDecisions.Count;
        var firstPassApproved = reviewDecisions.Count(status => status == TemplateStatusTransitions.Approved);
        var approvalRate = reviewed == 0 ? (double?)null : (double)firstPassApproved / reviewed;

        var lintPassed = await _db.TemplateLintResults.AsNoTracking()
            .Select(l => l.Passed)
            .ToListAsync(cancellationToken);

        var lintTotal = lintPassed.Count;
        var lintPassCount = lintPassed.Count(passed => passed);
        var lintPassRate = lintTotal == 0 ? (double?)null : (double)lintPassCount / lintTotal;

        return new TemplateApprovalMetricsDto(
            reviewed, firstPassApproved, approvalRate, lintTotal, lintPassCount, lintPassRate);
    }
}
