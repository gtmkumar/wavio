using System.Net;
using core.Application.Common;
using core.Application.Common.Interfaces;
using core.Application.Identity.Auth.Common;
using core.Application.Identity.Auth.Dtos;
using Wavio.Utilities.CQRS.Abstractions;
using wavio.SharedDataModel.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using RefreshTokenEntity = wavio.SharedDataModel.Entities.IdentityAccess.RefreshToken;
using LoginHistory = wavio.SharedDataModel.Entities.IdentityAccess.LoginHistory;

namespace core.Application.Identity.Auth.Commands.RefreshToken;

/// <summary>
/// Rotates a refresh token. On reuse detection (already revoked token presented),
/// revokes the entire family to protect against token theft.
/// </summary>
public sealed class RefreshTokenHandler : ICommandHandler<RefreshTokenCommand, TokenResponse>
{
    private readonly ICoreDbContext   _db;
    private readonly IJwtTokenService _jwt;
    private readonly JwtSettings      _jwtSettings;
    private readonly Microsoft.Extensions.Configuration.IConfiguration _config;

    public RefreshTokenHandler(
        ICoreDbContext db,
        IJwtTokenService jwt,
        IOptions<JwtSettings> jwtOptions,
        Microsoft.Extensions.Configuration.IConfiguration config)
    {
        _db          = db;
        _jwt         = jwt;
        _jwtSettings = jwtOptions.Value;
        _config      = config;
    }

    public async Task<TokenResponse> HandleAsync(RefreshTokenCommand cmd, CancellationToken ct)
    {
        var tokenHash = _jwt.HashRefreshToken(cmd.RawRefreshToken);

        var existing = await _db.RefreshTokens
            .Where(t => t.TokenHash == tokenHash)
            .FirstOrDefaultAsync(ct);

        if (existing is null)
            throw new UnauthorizedAccessException("Invalid refresh token.");

        // Reuse detection: token already revoked → revoke whole family
        if (existing.RevokedAt.HasValue)
        {
            await RevokeFamilyAsync(existing.FamilyId, "reuse_detected", ct);
            throw new UnauthorizedAccessException("Refresh token reuse detected. Please log in again.");
        }

        if (existing.ExpiresAt <= DateTimeOffset.UtcNow)
            throw new UnauthorizedAccessException("Refresh token expired.");

        if (!existing.UserId.HasValue)
            throw new UnauthorizedAccessException("Token has no associated user.");

        var user = await _db.Users.FindAsync([existing.UserId.Value], ct);
        if (user is null || user.Status is UserStatus.Suspended or UserStatus.Deleted)
            throw new UnauthorizedAccessException("User account is not active.");

        var ipAddress = string.IsNullOrEmpty(cmd.IpAddress) ? null
            : IPAddress.TryParse(cmd.IpAddress, out var ip) ? ip : null;

        // Revoke old token
        existing.RevokedAt     = DateTimeOffset.UtcNow;
        existing.RevokedReason = "rotated";

        // Issue new tokens
        var claims       = await ScopeResolver.BuildTokenClaimsAsync(
            _db, user, ct: ct);
        var accessToken  = _jwt.CreateAccessToken(claims);
        var rawRefresh   = _jwt.GenerateRefreshTokenRaw();
        var newTokenHash = _jwt.HashRefreshToken(rawRefresh);

        _db.RefreshTokens.Add(new RefreshTokenEntity
        {
            Id            = Guid.NewGuid(),
            UserId        = user.Id,
            TokenHash     = newTokenHash,
            FamilyId      = existing.FamilyId,
            ParentTokenId = existing.Id,
            IpAddress     = ipAddress,
            UserAgent     = cmd.UserAgent,
            IssuedAt      = DateTimeOffset.UtcNow,
            ExpiresAt     = DateTimeOffset.UtcNow.AddDays(_jwtSettings.RefreshDays),
            CreatedAt     = DateTimeOffset.UtcNow
        });

        _db.LoginHistories.Add(new LoginHistory
        {
            Id         = Guid.NewGuid(),
            UserId     = user.Id,
            Identifier = user.Email ?? user.PhoneE164 ?? user.Id.ToString(),
            AuthMethod = AuthMethod.Refresh,
            Success    = true,
            IpAddress  = ipAddress,
            UserAgent  = cmd.UserAgent,
            OccurredAt = DateTimeOffset.UtcNow,
            CreatedAt  = DateTimeOffset.UtcNow
        });

        await _db.SaveChangesAsync(ct);

        return new TokenResponse(
            AccessToken:      accessToken,
            RefreshToken:     rawRefresh,
            ExpiresInSeconds: _jwtSettings.AccessMinutes * 60);
    }

    private async Task RevokeFamilyAsync(Guid familyId, string reason, CancellationToken ct)
    {
        var familyTokens = await _db.RefreshTokens
            .Where(t => t.FamilyId == familyId && t.RevokedAt == null)
            .ToListAsync(ct);

        foreach (var t in familyTokens)
        {
            t.RevokedAt     = DateTimeOffset.UtcNow;
            t.RevokedReason = reason;
        }

        await _db.SaveChangesAsync(ct);
    }
}
