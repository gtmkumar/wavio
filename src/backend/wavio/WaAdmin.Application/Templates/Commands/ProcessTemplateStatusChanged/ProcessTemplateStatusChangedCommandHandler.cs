using System.Text.Json;
using WaAdmin.Application.Common.Interfaces;
using WaAdmin.Application.Templates.StateMachine;
using wavio.SharedDataModel.Entities.Templates;
using Wavio.Utilities.CQRS.Abstractions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace WaAdmin.Application.Templates.Commands.ProcessTemplateStatusChanged;

public sealed partial class ProcessTemplateStatusChangedCommandHandler
    : ICommandHandler<ProcessTemplateStatusChangedCommand, bool>
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly IWaAdminDbContext _db;
    private readonly ICampaignFreezeHook _campaignFreeze;
    private readonly ILogger<ProcessTemplateStatusChangedCommandHandler> _logger;

    public ProcessTemplateStatusChangedCommandHandler(
        IWaAdminDbContext db, ICampaignFreezeHook campaignFreeze,
        ILogger<ProcessTemplateStatusChangedCommandHandler> logger)
    {
        _db = db;
        _campaignFreeze = campaignFreeze;
        _logger = logger;
    }

    public async Task<bool> HandleAsync(ProcessTemplateStatusChangedCommand command, CancellationToken cancellationToken)
    {
        var evt = command.Event;

        // Wave 1 caveat (see WaIngest's normalizer notes): tenant resolution for webhook-sourced
        // events isn't built yet, so some deliveries carry TenantId = Guid.Empty. Park rather than
        // fabricate a tenant row or guess — the consumer dead-letters a false result.
        if (evt.TenantId == Guid.Empty)
        {
            LogParkedUnresolvableTenant(_logger, evt.MetaTemplateId);
            return false;
        }

        var template = await _db.Templates
            .FirstOrDefaultAsync(t => t.TenantId == evt.TenantId && t.MetaTemplateId == evt.MetaTemplateId, cancellationToken);
        if (template is null)
        {
            LogParkedUnknownTemplate(_logger, evt.MetaTemplateId, evt.TenantId);
            return false;
        }

        var newStatus = evt.NewStatus.ToUpperInvariant();
        if (!TemplateStatusTransitions.IsValidStatus(newStatus))
        {
            LogParkedUnknownStatus(_logger, evt.NewStatus, template.Id);
            return false;
        }

        if (!TemplateStatusTransitions.CanTransition(template.Status, newStatus))
        {
            LogParkedInvalidTransition(_logger, template.Status, newStatus, template.Id);
            return false;
        }

        var now = DateTimeOffset.UtcNow;
        var oldStatus = template.Status;

        var version = template.CurrentVersionId is null
            ? null
            : await _db.TemplateVersions.FirstOrDefaultAsync(v => v.Id == template.CurrentVersionId, cancellationToken);

        template.Status = newStatus;
        template.UpdatedAt = now;
        template.Version += 1;

        if (version is not null && TemplateStatusTransitions.CanTransition(version.Status, newStatus))
        {
            version.Status = newStatus;
            version.UpdatedAt = now;
            if (newStatus is TemplateStatusTransitions.Approved or TemplateStatusTransitions.Rejected)
                version.ReviewedAt = now;
            if (newStatus == TemplateStatusTransitions.Rejected)
                version.RejectionReason = evt.Reason;
        }

        switch (newStatus)
        {
            case TemplateStatusTransitions.Paused:
                template.PauseCount += 1;
                var duration = TemplateAutoPauseSchedule.DurationFor(template.PauseCount);
                template.PausedUntil = duration is null ? null : now + duration;
                await _campaignFreeze.FreezeCampaignsUsingTemplateAsync(template.Id, cancellationToken);
                break;

            case TemplateStatusTransitions.Disabled:
                template.PausedUntil = null;
                await _campaignFreeze.FreezeCampaignsUsingTemplateAsync(template.Id, cancellationToken);
                break;

            case TemplateStatusTransitions.Approved:
                template.PausedUntil = null;
                break;
        }

        _db.TemplateStatusEvents.Add(new TemplateStatusEvent
        {
            Id = Guid.NewGuid(),
            TenantId = evt.TenantId,
            TemplateId = template.Id,
            TemplateVersionId = version?.Id,
            OldStatus = oldStatus,
            NewStatus = newStatus,
            Reason = evt.Reason,
            Payload = JsonSerializer.Serialize(evt, JsonOptions),
            OccurredAt = evt.OccurredAt,
            CreatedAt = now,
            CreatedBy = null, // webhook-driven — no acting user
        });

        await _db.SaveChangesAsync(cancellationToken);
        return true;
    }

    [LoggerMessage(Level = LogLevel.Warning,
        Message = "Parked wa.template.status_changed.v1 for meta template {MetaTemplateId}: TenantId is unresolvable (Guid.Empty)")]
    private static partial void LogParkedUnresolvableTenant(ILogger logger, string metaTemplateId);

    [LoggerMessage(Level = LogLevel.Warning,
        Message = "Parked wa.template.status_changed.v1 for meta template {MetaTemplateId}: no matching template for tenant {TenantId}")]
    private static partial void LogParkedUnknownTemplate(ILogger logger, string metaTemplateId, Guid tenantId);

    [LoggerMessage(Level = LogLevel.Warning,
        Message = "Parked wa.template.status_changed.v1: unknown status '{NewStatus}' for template {TemplateId}")]
    private static partial void LogParkedUnknownStatus(ILogger logger, string newStatus, Guid templateId);

    [LoggerMessage(Level = LogLevel.Warning,
        Message = "Parked wa.template.status_changed.v1: invalid transition {OldStatus} -> {NewStatus} for template {TemplateId}")]
    private static partial void LogParkedInvalidTransition(ILogger logger, string oldStatus, string newStatus, Guid templateId);
}
