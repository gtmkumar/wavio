using System.Net;
using core.Application.Common;
using core.Application.Common.Interfaces;
using core.Application.Identity.Auth.Common;
using core.Application.Identity.Auth.Dtos;
using Wavio.Utilities.CQRS.Abstractions;
using wavio.SharedDataModel.Entities.IdentityAccess;
using wavio.SharedDataModel.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RefreshTokenEntity = wavio.SharedDataModel.Entities.IdentityAccess.RefreshToken;

namespace core.Application.Identity.Auth.Commands.PasswordLogin;

/// <summary>
/// Handles password-based login for system users.
/// On success: issues access JWT + rotating refresh token, resets failed_attempts, writes login_history.
/// On failure: increments failed_attempts, locks on threshold, writes login_history.
/// </summary>
public sealed class PasswordLoginHandler : ICommandHandler<PasswordLoginCommand, TokenResponse>
{
    private const int LockThreshold     = 5;
    private const int LockMinutes       = 15;

    private readonly ICoreDbContext          _db;
    private readonly IPasswordHasher         _hasher;
    private readonly IJwtTokenService        _jwt;
    private readonly IRefreshTokenRepository _refreshTokens;
    private readonly JwtSettings             _jwtSettings;
    private readonly IConfiguration          _config;
    private readonly ILogger<PasswordLoginHandler> _logger;

    public PasswordLoginHandler(
        ICoreDbContext db,
        IPasswordHasher hasher,
        IJwtTokenService jwt,
        IRefreshTokenRepository refreshTokens,
        IOptions<JwtSettings> jwtOptions,
        IConfiguration config,
        ILogger<PasswordLoginHandler> logger)
    {
        _db            = db;
        _hasher        = hasher;
        _jwt           = jwt;
        _refreshTokens = refreshTokens;
        _jwtSettings   = jwtOptions.Value;
        _config        = config;
        _logger        = logger;
    }

    public async Task<TokenResponse> HandleAsync(PasswordLoginCommand cmd, CancellationToken ct)
    {
        var identifier = cmd.Identifier.Trim();
        var ipAddress  = string.IsNullOrEmpty(cmd.IpAddress) ? null
            : IPAddress.TryParse(cmd.IpAddress, out var ip) ? ip : null;

        // Locate user by email or phone (ignoring soft-deleted via global filter)
        var user = await _db.Users
            .Where(u => u.Email == identifier || u.PhoneE164 == identifier)
            .FirstOrDefaultAsync(ct);

        if (user is null)
        {
            await WriteLoginHistory(null, identifier, false, "user_not_found", ipAddress, cmd.UserAgent, ct);
            throw new UnauthorizedAccessException("Invalid credentials.");
        }

        // Check lock
        if (user.LockedUntil.HasValue && user.LockedUntil > DateTimeOffset.UtcNow)
        {
            await WriteLoginHistory(user.Id, identifier, false, "account_locked", ipAddress, cmd.UserAgent, ct);
            throw new UnauthorizedAccessException($"Account locked until {user.LockedUntil:u}.");
        }

        // Check active status
        if (user.Status is UserStatus.Suspended or UserStatus.Deleted)
        {
            await WriteLoginHistory(user.Id, identifier, false, "account_suspended", ipAddress, cmd.UserAgent, ct);
            throw new UnauthorizedAccessException("Account is not active.");
        }

        // Verify password
        if (string.IsNullOrEmpty(user.PasswordHash) || !_hasher.Verify(cmd.Password, user.PasswordHash))
        {
            user.FailedAttempts++;
            if (user.FailedAttempts >= LockThreshold)
            {
                user.LockedUntil    = DateTimeOffset.UtcNow.AddMinutes(LockMinutes);
                user.Status         = UserStatus.Locked;
                user.FailedAttempts = 0;
                _logger.LogWarning("User {UserId} locked after {Threshold} failed attempts.", user.Id, LockThreshold);
            }
            user.UpdatedAt = DateTimeOffset.UtcNow;
            await _db.SaveChangesAsync(ct);
            await WriteLoginHistory(user.Id, identifier, false, "invalid_password", ipAddress, cmd.UserAgent, ct);
            throw new UnauthorizedAccessException("Invalid credentials.");
        }

        // Success — reset counters
        user.FailedAttempts = 0;
        user.LockedUntil    = null;
        user.LastLoginAt    = DateTimeOffset.UtcNow;
        user.LastLoginIp    = ipAddress;
        user.LastActiveAt   = DateTimeOffset.UtcNow;
        if (user.Status == UserStatus.Locked) user.Status = UserStatus.Active;
        user.UpdatedAt      = DateTimeOffset.UtcNow;

        // Build token claims
        var claims = await ScopeResolver.BuildTokenClaimsAsync(
            _db, user, ct: ct);
        var accessToken = _jwt.CreateAccessToken(claims);

        // Refresh token
        var rawRefresh  = _jwt.GenerateRefreshTokenRaw();
        var tokenHash   = _jwt.HashRefreshToken(rawRefresh);

        var refreshTokenId = Guid.NewGuid();
        var refreshToken = new RefreshTokenEntity
        {
            Id        = refreshTokenId,
            UserId    = user.Id,
            TokenHash = tokenHash,
            FamilyId  = refreshTokenId, // root: family_id = own id
            IpAddress = ipAddress,
            UserAgent = cmd.UserAgent,
            IssuedAt  = DateTimeOffset.UtcNow,
            ExpiresAt = DateTimeOffset.UtcNow.AddDays(_jwtSettings.RefreshDays),
            CreatedAt = DateTimeOffset.UtcNow
        };

        // Save user changes first (failed_attempts reset etc.)
        await _db.SaveChangesAsync(ct);
        // Then insert root refresh token via raw SQL (self-referential FK)
        await _refreshTokens.InsertRootAsync(refreshToken, ct);
        await WriteLoginHistory(user.Id, identifier, true, null, ipAddress, cmd.UserAgent, ct);

        return new TokenResponse(
            AccessToken:      accessToken,
            RefreshToken:     rawRefresh,
            ExpiresInSeconds: _jwtSettings.AccessMinutes * 60);
    }

    private async Task WriteLoginHistory(
        Guid? userId, string identifier, bool success,
        string? failureReason, IPAddress? ip, string? ua, CancellationToken ct)
    {
        _db.LoginHistories.Add(new LoginHistory
        {
            Id            = Guid.NewGuid(),
            UserId        = userId,
            Identifier    = identifier,
            AuthMethod    = AuthMethod.Password,
            Success       = success,
            FailureReason = failureReason,
            IpAddress     = ip,
            UserAgent     = ua,
            OccurredAt    = DateTimeOffset.UtcNow,
            CreatedAt     = DateTimeOffset.UtcNow
        });
        await _db.SaveChangesAsync(ct);
    }
}
