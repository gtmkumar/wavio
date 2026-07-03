using core.Application.Common.Interfaces;
using core.Application.Identity.Auth.Commands.ForgotPassword;
using Wavio.Utilities.CQRS.Abstractions;
using Microsoft.EntityFrameworkCore;

namespace core.Application.Identity.Auth.Commands.ResetPassword;

/// <summary>
/// Validates a password reset token, re-hashes the new password with Argon2id,
/// updates users.password_hash + password_changed_at, marks the reset token used.
/// </summary>
public sealed class ResetPasswordHandler : ICommandHandler<ResetPasswordCommand, bool>
{
    private readonly ICoreDbContext  _db;
    private readonly IPasswordHasher _hasher;

    public ResetPasswordHandler(ICoreDbContext db, IPasswordHasher hasher)
    {
        _db     = db;
        _hasher = hasher;
    }

    public async Task<bool> HandleAsync(ResetPasswordCommand cmd, CancellationToken ct)
    {
        var tokenHash = ForgotPasswordHandler.HashToken(cmd.Token);

        var reset = await _db.PasswordResets
            .Where(r => r.TokenHash == tokenHash
                     && r.UsedAt == null
                     && r.ExpiresAt > DateTimeOffset.UtcNow
                     && r.Status == "active")
            .FirstOrDefaultAsync(ct);

        if (reset is null)
            throw new UnauthorizedAccessException("Invalid or expired reset token.");

        var user = reset.UserId.HasValue
            ? await _db.Users.FindAsync([reset.UserId.Value], ct)
            : null;

        if (user is null)
            throw new UnauthorizedAccessException("User not found.");

        user.PasswordHash       = _hasher.Hash(cmd.NewPassword);
        user.PasswordChangedAt  = DateTimeOffset.UtcNow;
        user.MustChangePassword = false;
        user.UpdatedAt          = DateTimeOffset.UtcNow;

        reset.UsedAt   = DateTimeOffset.UtcNow;
        reset.Status   = "inactive";
        reset.UpdatedAt = DateTimeOffset.UtcNow;

        await _db.SaveChangesAsync(ct);
        return true;
    }
}
