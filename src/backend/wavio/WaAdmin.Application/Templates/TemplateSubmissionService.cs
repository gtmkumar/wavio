using WaAdmin.Application.Common.Interfaces;
using WaAdmin.Application.Templates.StateMachine;
using wavio.SharedDataModel.Entities.Templates;
using wavio.Utilities.Exceptions;
using Microsoft.Extensions.Logging;

namespace WaAdmin.Application.Templates;

public sealed partial class TemplateSubmissionService : ITemplateSubmissionService
{
    private readonly IWaAdminDbContext _db;
    private readonly IWhatsAppTemplateGraphClient _graph;
    private readonly IEnumerable<ITemplateLintService> _linters;
    private readonly ILogger<TemplateSubmissionService> _logger;

    public TemplateSubmissionService(
        IWaAdminDbContext db, IWhatsAppTemplateGraphClient graph, IEnumerable<ITemplateLintService> linters,
        ILogger<TemplateSubmissionService> logger)
    {
        _db = db;
        _graph = graph;
        _linters = linters;
        _logger = logger;
    }

    public async Task<TemplateSubmissionOutcome> SubmitAsync(
        Template templateToSubmit, TemplateVersion version, Guid? actorId, CancellationToken cancellationToken)
    {
        if (version.Status != TemplateStatusTransitions.Draft
            || !TemplateStatusTransitions.CanTransition(templateToSubmit.Status, TemplateStatusTransitions.Pending))
        {
            throw new BusinessRuleException(
                $"Template '{templateToSubmit.Id}' is '{templateToSubmit.Status}' — only a DRAFT template with " +
                "a DRAFT version can be submitted.");
        }

        // Every submission path — the create-and-submit flow and the standalone resubmit flow
        // alike — runs every registered linter here, right before the Graph call. This is the
        // single enforcement point (issue #27): a version edited via UpdateTemplateCommandHandler
        // and then explicitly resubmitted via POST .../submit gets linted exactly the same as one
        // submitted immediately on create. Lint rows are persisted whether or not they block, so
        // the audit trail survives even a blocked submission attempt.
        var lintInput = new TemplateLintInput(templateToSubmit.Category, templateToSubmit.Language, version.Components);
        var lintNow = DateTimeOffset.UtcNow;
        var blockingLinters = new List<string>();
        foreach (var linter in _linters)
        {
            var lintOutcome = await linter.LintAsync(lintInput, cancellationToken);
            _db.TemplateLintResults.Add(new TemplateLintResult
            {
                Id = Guid.NewGuid(),
                TenantId = templateToSubmit.TenantId,
                TemplateVersionId = version.Id,
                Linter = linter.Linter,
                Passed = lintOutcome.Passed,
                Findings = lintOutcome.Findings,
                Score = lintOutcome.Score,
                CreatedAt = lintNow,
                CreatedBy = actorId,
            });
            if (!lintOutcome.Passed)
                blockingLinters.Add(linter.Linter);
        }
        await _db.SaveChangesAsync(cancellationToken);

        if (blockingLinters.Count > 0)
        {
            return new TemplateSubmissionOutcome(
                false,
                $"Lint failed ({string.Join(", ", blockingLinters)}); resolve findings before submitting.");
        }

        var metaWabaId = await _db.GetBusinessAccountMetaWabaIdAsync(templateToSubmit.BusinessAccountId, cancellationToken);
        if (metaWabaId is null)
            throw new BusinessRuleException("The template's business account was not found.");

        GraphTemplateSubmitResult result;
        try
        {
            result = await _graph.SubmitTemplateAsync(
                new GraphTemplateSubmitRequest(
                    metaWabaId, templateToSubmit.Name, templateToSubmit.Language, templateToSubmit.Category,
                    version.Components),
                cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Degraded-mode contract: a Graph API/network failure never corrupts local state —
            // the template stays DRAFT, resubmit is always safe to retry.
            LogSubmissionFailed(_logger, ex, templateToSubmit.Id);
            return new TemplateSubmissionOutcome(false, $"Submission to Meta failed: {ex.Message}");
        }

        if (!result.Accepted)
            return new TemplateSubmissionOutcome(false, result.ErrorMessage ?? "Meta rejected the submission.");

        var now = DateTimeOffset.UtcNow;
        var oldStatus = templateToSubmit.Status;

        templateToSubmit.Status = TemplateStatusTransitions.Pending;
        templateToSubmit.MetaTemplateId = result.MetaTemplateId;
        templateToSubmit.UpdatedAt = now;
        templateToSubmit.UpdatedBy = actorId;
        templateToSubmit.Version += 1;

        version.Status = TemplateStatusTransitions.Pending;
        version.SubmittedAt = now;
        version.UpdatedAt = now;
        version.UpdatedBy = actorId;

        _db.TemplateStatusEvents.Add(new TemplateStatusEvent
        {
            Id = Guid.NewGuid(),
            TenantId = templateToSubmit.TenantId,
            TemplateId = templateToSubmit.Id,
            TemplateVersionId = version.Id,
            OldStatus = oldStatus,
            NewStatus = TemplateStatusTransitions.Pending,
            Reason = "submitted_to_meta",
            OccurredAt = now,
            CreatedAt = now,
            CreatedBy = actorId,
        });

        await _db.SaveChangesAsync(cancellationToken);
        return new TemplateSubmissionOutcome(true, null);
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Meta template submission failed for template {TemplateId}")]
    private static partial void LogSubmissionFailed(ILogger logger, Exception exception, Guid templateId);
}
