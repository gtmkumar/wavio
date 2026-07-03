using core.Application.Common.Interfaces;
using Wavio.Utilities.CQRS.Abstractions;
using wavio.SharedDataModel.Enums;

namespace core.Application.Identity.Users.Commands.DeactivateUser;

public sealed record DeactivateUserCommand(Guid Id, Guid? ActorId) : ICommand<bool>;

public class DeactivateUserCommandHandler : ICommandHandler<DeactivateUserCommand, bool>
{
    private readonly ICoreDbContext _db;
    public DeactivateUserCommandHandler(ICoreDbContext db) => _db = db;

    public async Task<bool> HandleAsync(DeactivateUserCommand cmd, CancellationToken ct)
    {
        var user = await _db.Users.FindAsync([cmd.Id], ct);
        if (user is null) return false;
        user.Status    = UserStatus.Suspended;
        user.UpdatedAt = DateTimeOffset.UtcNow;
        user.UpdatedBy = cmd.ActorId;
        user.Version++;
        await _db.SaveChangesAsync(ct);
        return true;
    }
}
