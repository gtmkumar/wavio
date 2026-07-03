using core.Application.Common.Interfaces;
using Wavio.Utilities.CQRS.Abstractions;
using Microsoft.EntityFrameworkCore;

namespace core.Application.Identity.Auth.Commands.Logout;

/// <summary>Revokes all tokens in the refresh token's family (full session logout).</summary>
public sealed class LogoutHandler : ICommandHandler<LogoutCommand, bool>
{
    private readonly ICoreDbContext   _db;
    private readonly IJwtTokenService _jwt;

    public LogoutHandler(ICoreDbContext db, IJwtTokenService jwt)
    {
        _db  = db;
        _jwt = jwt;
    }

    public async Task<bool> HandleAsync(LogoutCommand cmd, CancellationToken ct)
    {
        var tokenHash = _jwt.HashRefreshToken(cmd.RawRefreshToken);

        var token = await _db.RefreshTokens
            .Where(t => t.TokenHash == tokenHash)
            .FirstOrDefaultAsync(ct);

        if (token is null) return true; // idempotent

        // Revoke entire family
        var family = await _db.RefreshTokens
            .Where(t => t.FamilyId == token.FamilyId && t.RevokedAt == null)
            .ToListAsync(ct);

        foreach (var t in family)
        {
            t.RevokedAt     = DateTimeOffset.UtcNow;
            t.RevokedReason = "logout";
        }

        await _db.SaveChangesAsync(ct);
        return true;
    }
}
