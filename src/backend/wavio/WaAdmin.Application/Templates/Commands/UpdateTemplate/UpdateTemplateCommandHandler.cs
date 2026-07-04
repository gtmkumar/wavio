using WaAdmin.Application.Common.Interfaces;
using WaAdmin.Application.Templates.Dtos;
using WaAdmin.Application.Templates.StateMachine;
using wavio.SharedDataModel.Entities.Templates;
using wavio.Utilities.Exceptions;
using Wavio.Utilities.CQRS.Abstractions;
using Microsoft.EntityFrameworkCore;

namespace WaAdmin.Application.Templates.Commands.UpdateTemplate;

public sealed class UpdateTemplateCommandHandler : ICommandHandler<UpdateTemplateCommand, TemplateDto?>
{
    private readonly IWaAdminDbContext _db;

    public UpdateTemplateCommandHandler(IWaAdminDbContext db) => _db = db;

    public async Task<TemplateDto?> HandleAsync(UpdateTemplateCommand command, CancellationToken cancellationToken)
    {
        var template = await _db.Templates.FirstOrDefaultAsync(t => t.Id == command.TemplateId, cancellationToken);
        if (template is null) return null;

        // Mid-review: Meta already has this content; editing now would desync what Meta is
        // evaluating from what the tenant sees. Terminal: DISABLED templates are dead.
        if (template.Status is TemplateStatusTransitions.Pending or TemplateStatusTransitions.Disabled)
            throw new BusinessRuleException(
                $"Template '{template.Id}' is '{template.Status}' and cannot be edited.");

        var definition = command.Request.Definition;
        var errors = new Dictionary<string, string[]>();
        if (definition.Components.Count == 0) errors["components"] = ["At least one component is required."];
        // Name/language are Meta's template identity key — renaming is not an "edit", it is a new
        // template. Reject a silent identity change rather than accepting it and quietly
        // desyncing from Meta on the next submit.
        if (!string.Equals(definition.Name, template.Name, StringComparison.Ordinal))
            errors["name"] = ["Name cannot be changed after creation; create a new template instead."];
        if (!string.Equals(definition.Language, template.Language, StringComparison.Ordinal))
            errors["language"] = ["Language cannot be changed after creation; create a new template instead."];
        if (errors.Count > 0) throw new ValidationException(errors);

        var componentsJson = TemplateDefinitionCompiler.CompileComponents(definition);
        var category = TemplateDefinitionCompiler.CompileCategory(definition.Category);
        var now = DateTimeOffset.UtcNow;

        var currentVersion = template.CurrentVersionId is null
            ? null
            : await _db.TemplateVersions.FirstOrDefaultAsync(v => v.Id == template.CurrentVersionId, cancellationToken);

        TemplateVersion resultVersion;

        if (currentVersion is null || currentVersion.Status == TemplateStatusTransitions.Draft)
        {
            // In-place edit: nothing has been submitted/reviewed for this content yet.
            if (currentVersion is null)
            {
                currentVersion = new TemplateVersion
                {
                    Id = Guid.NewGuid(),
                    TenantId = command.TenantId,
                    TemplateId = template.Id,
                    VersionNumber = 1,
                    Status = TemplateStatusTransitions.Draft,
                    CreatedAt = now,
                    UpdatedAt = now,
                    CreatedBy = command.ActorId,
                    UpdatedBy = command.ActorId,
                };
                _db.TemplateVersions.Add(currentVersion);
                template.CurrentVersionId = currentVersion.Id;
            }

            currentVersion.Components = componentsJson;
            currentVersion.ExampleValues = command.Request.ExampleValuesJson;
            currentVersion.UpdatedAt = now;
            currentVersion.UpdatedBy = command.ActorId;
            resultVersion = currentVersion;
        }
        else
        {
            // Immutability (issue #16 Task 6): never mutate a reviewed version's content — a new
            // DRAFT version carries the edit, and the template as a whole re-enters DRAFT so it
            // can be resubmitted via POST /v1/templates/{id}/submit.
            if (!TemplateStatusTransitions.CanTransition(template.Status, TemplateStatusTransitions.Draft))
                throw new BusinessRuleException(
                    $"Template '{template.Id}' is '{template.Status}' and cannot be edited.");

            // Nullable Max (not Max+DefaultIfEmpty) — the DefaultIfEmpty/Max combination isn't
            // translatable by every provider (confirmed failing against EF's InMemory provider
            // in tests); MaxAsync over a nullable projection is the portable pattern for
            // "0 when the sequence is empty" and only ever hits this branch when at least one
            // version already exists (currentVersion is non-null here), so empty is defensive.
            var nextVersionNumber = (await _db.TemplateVersions
                .Where(v => v.TemplateId == template.Id)
                .MaxAsync(v => (int?)v.VersionNumber, cancellationToken) ?? 0) + 1;

            var newVersion = new TemplateVersion
            {
                Id = Guid.NewGuid(),
                TenantId = command.TenantId,
                TemplateId = template.Id,
                VersionNumber = nextVersionNumber,
                Components = componentsJson,
                ExampleValues = command.Request.ExampleValuesJson,
                Status = TemplateStatusTransitions.Draft,
                CreatedAt = now,
                UpdatedAt = now,
                CreatedBy = command.ActorId,
                UpdatedBy = command.ActorId,
            };
            _db.TemplateVersions.Add(newVersion);

            var oldStatus = template.Status;
            template.CurrentVersionId = newVersion.Id;
            template.Status = TemplateStatusTransitions.Draft;

            _db.TemplateStatusEvents.Add(new TemplateStatusEvent
            {
                Id = Guid.NewGuid(),
                TenantId = command.TenantId,
                TemplateId = template.Id,
                TemplateVersionId = newVersion.Id,
                OldStatus = oldStatus,
                NewStatus = TemplateStatusTransitions.Draft,
                Reason = "edited_new_version",
                OccurredAt = now,
                CreatedAt = now,
                CreatedBy = command.ActorId,
            });

            resultVersion = newVersion;
        }

        template.Category = category;
        template.UpdatedAt = now;
        template.UpdatedBy = command.ActorId;
        template.Version += 1;

        await _db.SaveChangesAsync(cancellationToken);
        return template.ToDto(resultVersion);
    }
}
