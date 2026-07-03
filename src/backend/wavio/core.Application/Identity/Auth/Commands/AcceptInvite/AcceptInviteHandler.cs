using core.Application.Common.Interfaces;
using Wavio.Utilities.CQRS.Abstractions;
using wavio.SharedDataModel.Enums;
using wavio.Utilities.Exceptions;
using Microsoft.EntityFrameworkCore;

namespace core.Application.Identity.Auth.Commands.AcceptInvite;

public sealed class AcceptInviteHandler : ICommandHandler<AcceptInviteCommand, bool>
{
    private readonly ICoreDbContext _db;
    private readonly IPasswordHasher _hasher;
    public AcceptInviteHandler(ICoreDbContext db, IPasswordHasher hasher) { _db = db; _hasher = hasher; }

    public async Task<bool> HandleAsync(AcceptInviteCommand cmd, CancellationToken ct)
    {
        var r = cmd.Request;
        if (string.IsNullOrWhiteSpace(r.NewPassword) || r.NewPassword.Length < 8)
            throw new ValidationException(new Dictionary<string, string[]>
                { ["newPassword"] = ["Password must be at least 8 characters."] });

        var user = await _db.Users
            .FirstOrDefaultAsync(u => u.InvitationToken == r.Token && u.Status == UserStatus.Invited, ct);
        if (user is null)
            throw new ValidationException(new Dictionary<string, string[]>
                { ["token"] = ["This invitation is invalid or has already been used."] });

        var now = DateTimeOffset.UtcNow;
        user.PasswordHash        = _hasher.Hash(r.NewPassword);
        user.PasswordChangedAt   = now;
        user.MustChangePassword  = false;
        user.Status              = UserStatus.Active;
        user.InvitationToken     = null;
        user.InvitationAcceptedAt = now;
        user.UpdatedAt           = now;
        user.Version++;
        await _db.SaveChangesAsync(ct);
        return true;
    }
}
