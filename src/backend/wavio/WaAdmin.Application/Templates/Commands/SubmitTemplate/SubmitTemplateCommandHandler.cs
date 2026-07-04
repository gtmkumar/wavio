using WaAdmin.Application.Common.Interfaces;
using WaAdmin.Application.Templates.Dtos;
using wavio.Utilities.Exceptions;
using Wavio.Utilities.CQRS.Abstractions;
using Microsoft.EntityFrameworkCore;

namespace WaAdmin.Application.Templates.Commands.SubmitTemplate;

public sealed class SubmitTemplateCommandHandler : ICommandHandler<SubmitTemplateCommand, CreateTemplateResult>
{
    private readonly IWaAdminDbContext _db;
    private readonly ITemplateSubmissionService _submission;

    public SubmitTemplateCommandHandler(IWaAdminDbContext db, ITemplateSubmissionService submission)
    {
        _db = db;
        _submission = submission;
    }

    public async Task<CreateTemplateResult> HandleAsync(SubmitTemplateCommand command, CancellationToken cancellationToken)
    {
        var template = await _db.Templates.FirstOrDefaultAsync(t => t.Id == command.TemplateId, cancellationToken)
            ?? throw new KeyNotFoundException($"Template '{command.TemplateId}' was not found.");

        if (template.CurrentVersionId is null)
            throw new BusinessRuleException($"Template '{template.Id}' has no version to submit.");

        var version = await _db.TemplateVersions.FirstOrDefaultAsync(v => v.Id == template.CurrentVersionId, cancellationToken)
            ?? throw new BusinessRuleException($"Template '{template.Id}' has no version to submit.");

        var outcome = await _submission.SubmitAsync(template, version, command.ActorId, cancellationToken);
        return new CreateTemplateResult(template.ToDto(version), outcome.Submitted, outcome.Error);
    }
}
