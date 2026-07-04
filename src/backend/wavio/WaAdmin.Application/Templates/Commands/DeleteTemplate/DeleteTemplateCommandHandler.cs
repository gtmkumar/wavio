using WaAdmin.Application.Common.Interfaces;
using WaAdmin.Application.Templates.StateMachine;
using wavio.Utilities.Exceptions;
using Wavio.Utilities.CQRS.Abstractions;
using Microsoft.EntityFrameworkCore;

namespace WaAdmin.Application.Templates.Commands.DeleteTemplate;

public sealed class DeleteTemplateCommandHandler : ICommandHandler<DeleteTemplateCommand, bool>
{
    private readonly IWaAdminDbContext _db;

    public DeleteTemplateCommandHandler(IWaAdminDbContext db) => _db = db;

    public async Task<bool> HandleAsync(DeleteTemplateCommand command, CancellationToken cancellationToken)
    {
        var template = await _db.Templates.FirstOrDefaultAsync(t => t.Id == command.TemplateId, cancellationToken);
        if (template is null) return false;

        if (template.Status != TemplateStatusTransitions.Draft)
            throw new BusinessRuleException(
                $"Template '{template.Id}' is '{template.Status}'; only a DRAFT template can be deleted.");

        template.DeletedAt = DateTimeOffset.UtcNow;
        template.UpdatedAt = template.DeletedAt.Value;
        template.UpdatedBy = command.ActorId;
        template.Version += 1;

        await _db.SaveChangesAsync(cancellationToken);
        return true;
    }
}
