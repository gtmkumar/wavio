using WaAdmin.Application.Common.Interfaces;
using WaAdmin.Application.Templates.StateMachine;
using wavio.SharedDataModel.Entities.Templates;
using wavio.Utilities.Exceptions;
using Wavio.Utilities.CQRS.Abstractions;
using Microsoft.EntityFrameworkCore;

namespace WaAdmin.Application.Templates.Commands.CreateTemplate;

public sealed class CreateTemplateCommandHandler : ICommandHandler<CreateTemplateCommand, Dtos.CreateTemplateResult>
{
    private readonly IWaAdminDbContext _db;
    private readonly ITemplateSubmissionService _submission;

    public CreateTemplateCommandHandler(IWaAdminDbContext db, ITemplateSubmissionService submission)
    {
        _db = db;
        _submission = submission;
    }

    public async Task<Dtos.CreateTemplateResult> HandleAsync(CreateTemplateCommand command, CancellationToken cancellationToken)
    {
        var req = command.Request;
        var definition = req.Definition;

        // Manual guard clauses, not a FluentValidation validator: this codebase's CQRS
        // ValidationBehavior is registered in DI (BehaviorRegistrar) but never wired into the
        // active Dispatcher (see wavio.Utilities/CQRS/Dispatcher/Dispatcher.cs — no pipeline
        // execution), so an AbstractValidator<CreateTemplateCommand> would silently never run.
        // core.Application's CreateUserCommandHandler follows the same manual-guard convention.
        var errors = new Dictionary<string, string[]>();
        if (string.IsNullOrWhiteSpace(definition.Name))
            errors["name"] = ["Name is required."];
        if (string.IsNullOrWhiteSpace(definition.Language))
            errors["language"] = ["Language is required."];
        if (definition.Components.Count == 0)
            errors["components"] = ["At least one component is required."];
        if (req.BusinessAccountId == Guid.Empty)
            errors["businessAccountId"] = ["BusinessAccountId is required."];
        if (errors.Count > 0)
            throw new ValidationException(errors);

        // Defense in depth: the DB's UNIQUE (business_account_id, name, language) index is the
        // real guard (a race here still surfaces as a clean 409 via ExceptionHandler's
        // DbUpdateException mapping) — this pre-check just gives a faster, friendlier error on
        // the common non-racy path.
        var duplicate = await _db.Templates.AnyAsync(
            t => t.BusinessAccountId == req.BusinessAccountId
              && t.Name == definition.Name
              && t.Language == definition.Language,
            cancellationToken);
        if (duplicate)
            throw new BusinessRuleException(
                $"A template named '{definition.Name}' already exists for this business account and language.");

        var now = DateTimeOffset.UtcNow;
        var componentsJson = TemplateDefinitionCompiler.CompileComponents(definition);
        var category = TemplateDefinitionCompiler.CompileCategory(definition.Category);

        var template = new Template
        {
            Id = Guid.NewGuid(),
            TenantId = command.TenantId,
            BusinessAccountId = req.BusinessAccountId,
            Name = definition.Name,
            Language = definition.Language,
            Category = category,
            Status = TemplateStatusTransitions.Draft,
            PauseCount = 0,
            CreatedAt = now,
            UpdatedAt = now,
            CreatedBy = command.ActorId,
            UpdatedBy = command.ActorId,
            Version = 1,
        };
        _db.Templates.Add(template);

        var version = new TemplateVersion
        {
            Id = Guid.NewGuid(),
            TenantId = command.TenantId,
            TemplateId = template.Id,
            VersionNumber = 1,
            Components = componentsJson,
            ExampleValues = req.ExampleValuesJson,
            Status = TemplateStatusTransitions.Draft,
            CreatedAt = now,
            UpdatedAt = now,
            CreatedBy = command.ActorId,
            UpdatedBy = command.ActorId,
        };
        _db.TemplateVersions.Add(version);

        // Template <-> TemplateVersion is a genuine circular FK pair (see TemplateConfiguration's
        // comment) that EF cannot insert in one batch when both rows are brand new — save them
        // first, then point CurrentVersionId at the now-existing version in a follow-up save.
        await _db.SaveChangesAsync(cancellationToken);
        template.CurrentVersionId = version.Id;
        await _db.SaveChangesAsync(cancellationToken);

        // Lint gating lives entirely in TemplateSubmissionService.SubmitAsync (issue #27) — it
        // runs every registered linter, persists a template_lint_results row per linter, and
        // withholds the Graph submission (never the row itself — creation still succeeds) when
        // any linter blocks. This is also what makes the standalone POST .../submit resubmit path
        // (SubmitTemplateCommandHandler) lint-gated identically, not just the create-and-submit
        // flow here.
        var outcome = await _submission.SubmitAsync(template, version, command.ActorId, cancellationToken);
        return new Dtos.CreateTemplateResult(template.ToDto(version), outcome.Submitted, outcome.Error);
    }
}
