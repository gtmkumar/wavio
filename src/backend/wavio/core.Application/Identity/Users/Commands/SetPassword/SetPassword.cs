using core.Application.Common.Interfaces;
using core.Application.Identity.Users.Dtos;
using Wavio.Utilities.CQRS.Abstractions;

namespace core.Application.Identity.Users.Commands.SetPassword;

public sealed record SetPasswordCommand(Guid UserId, SetPasswordRequest Request, Guid? ActorId) : ICommand<bool>;

public class SetPasswordCommandHandler : ICommandHandler<SetPasswordCommand, bool>
{
    private readonly ICoreDbContext _db;
    private readonly IPasswordHasher _hasher;
    public SetPasswordCommandHandler(ICoreDbContext db, IPasswordHasher hasher) { _db = db; _hasher = hasher; }

    public async Task<bool> HandleAsync(SetPasswordCommand cmd, CancellationToken ct)
    {
        var user = await _db.Users.FindAsync([cmd.UserId], ct);
        if (user is null) return false;
        user.PasswordHash       = _hasher.Hash(cmd.Request.NewPassword);
        user.PasswordChangedAt  = DateTimeOffset.UtcNow;
        user.MustChangePassword = false;
        user.UpdatedAt          = DateTimeOffset.UtcNow;
        user.UpdatedBy          = cmd.ActorId;
        user.Version++;
        await _db.SaveChangesAsync(ct);
        return true;
    }
}
