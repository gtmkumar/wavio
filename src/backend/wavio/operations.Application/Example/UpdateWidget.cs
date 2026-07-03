using operations.Application.Common.Interfaces;
using Wavio.Utilities.CQRS.Abstractions;
using wavio.Utilities.Services;

namespace operations.Application.Example;

public sealed record UpdateWidgetCommand(Guid Id, UpdateWidgetRequest Request) : ICommand<bool>;

public class UpdateWidgetCommandHandler : ICommandHandler<UpdateWidgetCommand, bool>
{
    private readonly IOperationsDbContext _db;
    private readonly ICurrentUser _user;
    public UpdateWidgetCommandHandler(IOperationsDbContext db, ICurrentUser user) { _db = db; _user = user; }

    public async Task<bool> HandleAsync(UpdateWidgetCommand cmd, CancellationToken ct)
    {
        var widget = await _db.Widgets.FindAsync([cmd.Id], ct);
        if (widget is null) return false;

        widget.Name        = cmd.Request.Name;
        widget.Description = cmd.Request.Description;
        widget.Status       = cmd.Request.Status;
        widget.UpdatedAt    = DateTimeOffset.UtcNow;
        widget.UpdatedBy    = _user.UserId;
        widget.Version++;

        await _db.SaveChangesAsync(ct);
        return true;
    }
}
