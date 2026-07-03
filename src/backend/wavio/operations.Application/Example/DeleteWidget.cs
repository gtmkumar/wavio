using operations.Application.Common.Interfaces;
using Wavio.Utilities.CQRS.Abstractions;

namespace operations.Application.Example;

public sealed record DeleteWidgetCommand(Guid Id) : ICommand<bool>;

public class DeleteWidgetCommandHandler : ICommandHandler<DeleteWidgetCommand, bool>
{
    private readonly IOperationsDbContext _db;
    public DeleteWidgetCommandHandler(IOperationsDbContext db) { _db = db; }

    public async Task<bool> HandleAsync(DeleteWidgetCommand cmd, CancellationToken ct)
    {
        var widget = await _db.Widgets.FindAsync([cmd.Id], ct);
        if (widget is null) return false;

        widget.DeletedAt = DateTimeOffset.UtcNow;
        await _db.SaveChangesAsync(ct);
        return true;
    }
}
