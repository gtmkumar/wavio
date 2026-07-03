using System.Text.Json;
using WaAdmin.Application.Common.Interfaces;
using wavio.SharedDataModel.Entities.Templates;
using Wavio.Utilities.CQRS.Abstractions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace WaAdmin.Application.Templates.Commands.ProcessTemplateCategoryChanged;

public sealed partial class ProcessTemplateCategoryChangedCommandHandler
    : ICommandHandler<ProcessTemplateCategoryChangedCommand, bool>
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly IWaAdminDbContext _db;
    private readonly ITenantAlertPublisher _alerts;
    private readonly IBillingRecalibrationHook _billing;
    private readonly ILogger<ProcessTemplateCategoryChangedCommandHandler> _logger;

    public ProcessTemplateCategoryChangedCommandHandler(
        IWaAdminDbContext db, ITenantAlertPublisher alerts, IBillingRecalibrationHook billing,
        ILogger<ProcessTemplateCategoryChangedCommandHandler> logger)
    {
        _db = db;
        _alerts = alerts;
        _billing = billing;
        _logger = logger;
    }

    public async Task<bool> HandleAsync(ProcessTemplateCategoryChangedCommand command, CancellationToken cancellationToken)
    {
        var evt = command.Event;

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

        var now = DateTimeOffset.UtcNow;
        var oldCategory = template.Category;
        var newCategory = evt.NewCategory.ToLowerInvariant();

        template.Category = newCategory;
        template.UpdatedAt = now;
        template.Version += 1;

        var change = new TemplateCategoryChange
        {
            Id = Guid.NewGuid(),
            TenantId = evt.TenantId,
            TemplateId = template.Id,
            OldCategory = oldCategory,
            NewCategory = newCategory,
            OccurredAt = evt.OccurredAt,
            Payload = JsonSerializer.Serialize(evt, JsonOptions),
            CreatedAt = now,
            UpdatedAt = now,
        };
        _db.TemplateCategoryChanges.Add(change);

        // Two mandatory reactions (spec §4.4). Both are stubs in Wave 1 (no real notification
        // channel; billing doesn't exist until #19) but the pipeline shape — always call them,
        // always record when they ran — is real, so #19 only needs to swap the implementation.
        await _alerts.RaiseAsync(
            evt.TenantId, "template.category_changed",
            $"Template '{template.Name}' ({template.Language}) was recategorized from " +
            $"{oldCategory} to {newCategory}; this changes its per-message price.", cancellationToken);
        change.TenantAlertedAt = now;

        await _billing.RecalibrateAsync(template.Id, oldCategory, newCategory, cancellationToken);
        change.BillingRecalibratedAt = now;

        await _db.SaveChangesAsync(cancellationToken);
        return true;
    }

    [LoggerMessage(Level = LogLevel.Warning,
        Message = "Parked wa.template.category_changed.v1 for meta template {MetaTemplateId}: TenantId is unresolvable (Guid.Empty)")]
    private static partial void LogParkedUnresolvableTenant(ILogger logger, string metaTemplateId);

    [LoggerMessage(Level = LogLevel.Warning,
        Message = "Parked wa.template.category_changed.v1 for meta template {MetaTemplateId}: no matching template for tenant {TenantId}")]
    private static partial void LogParkedUnknownTemplate(ILogger logger, string metaTemplateId, Guid tenantId);
}
