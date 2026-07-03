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
    private readonly ILogger<TemplateSubmissionService> _logger;

    public TemplateSubmissionService(
        IWaAdminDbContext db, IWhatsAppTemplateGraphClient graph, ILogger<TemplateSubmissionService> logger)
    {
        _db = db;
        _graph = graph;
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
