using operations.Application.Common.Interfaces;
using Wavio.Utilities.CQRS.Abstractions;
using wavio.SharedDataModel.Entities.Example;
using wavio.Utilities.Services;

namespace operations.Application.Example;

public sealed record CreateWidgetCommand(CreateWidgetRequest Request) : ICommand<WidgetDto>;

public class CreateWidgetCommandHandler : ICommandHandler<CreateWidgetCommand, WidgetDto>
{
    private readonly IOperationsDbContext _db;
    private readonly ICurrentUser _user;
    public CreateWidgetCommandHandler(IOperationsDbContext db, ICurrentUser user) { _db = db; _user = user; }

    public async Task<WidgetDto> HandleAsync(CreateWidgetCommand cmd, CancellationToken ct)
    {
        var widget = new Widget
        {
            Id          = Guid.NewGuid(),
            TenantId    = _user.RequireTenantId(),
            Name        = cmd.Request.Name,
            Description = cmd.Request.Description,
            Status      = "active",
            CreatedAt   = DateTimeOffset.UtcNow,
            UpdatedAt   = DateTimeOffset.UtcNow,
            CreatedBy   = _user.UserId,
            Version     = 1
        };
        _db.Widgets.Add(widget);
        await _db.SaveChangesAsync(ct);

        return new WidgetDto(widget.Id, widget.TenantId, widget.Name, widget.Description, widget.Status, widget.CreatedAt);
    }
}
