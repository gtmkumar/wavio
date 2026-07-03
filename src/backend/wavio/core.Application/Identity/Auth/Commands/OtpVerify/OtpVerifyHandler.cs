using System.Net;
using core.Application.Common;
using core.Application.Common.Interfaces;
using core.Application.Identity.Auth.Common;
using core.Application.Identity.Auth.Dtos;
using Wavio.Utilities.CQRS.Abstractions;
using wavio.SharedDataModel.Entities.IdentityAccess;
using wavio.SharedDataModel.Enums;
using wavio.Utilities.Exceptions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using RefreshTokenEntity = wavio.SharedDataModel.Entities.IdentityAccess.RefreshToken;

namespace core.Application.Identity.Auth.Commands.OtpVerify;

/// <summary>
/// Verifies an OTP code. On success marks verified_at, updates user phone/email verification,
/// issues JWT + refresh token.
///
/// Security properties enforced:
///   SEC1 — Rolling-window lockout check before looking up the OTP row (no oracle on existence).
///   SEC2 — HMAC-SHA256 verify with per-row salt; falls back to legacy SHA-256 for rows
///           written before the salt migration (code_salt IS NULL).
/// </summary>
public sealed class OtpVerifyHandler : ICommandHandler<OtpVerifyCommand, OtpVerifiedResponse>
{
    private readonly ICoreDbContext          _db;
    private readonly IJwtTokenService        _jwt;
    private readonly IRefreshTokenRepository _refreshTokens;
    private readonly JwtSettings             _jwtSettings;
    private readonly OtpSettings             _otpSettings;
    private readonly IHostEnvironment        _env;
    private readonly Microsoft.Extensions.Configuration.IConfiguration _config;

    public OtpVerifyHandler(
        ICoreDbContext db,
        IJwtTokenService jwt,
        IRefreshTokenRepository refreshTokens,
        IOptions<JwtSettings> jwtOptions,
        IOptions<OtpSettings> otpOptions,
        IHostEnvironment env,
        Microsoft.Extensions.Configuration.IConfiguration config)
    {
        _db            = db;
        _jwt           = jwt;
        _refreshTokens = refreshTokens;
        _jwtSettings   = jwtOptions.Value;
        _otpSettings   = otpOptions.Value;
        _env           = env;
        _config        = config;
    }

    /// <summary>
    /// DEFECT 8 — pure decision for what a successful OTP verification does to the
    /// user's account status. Split out so the rules are unit-testable.
    /// Returns (blocked, nextStatus): when <c>blocked</c> is true the login must be
    /// refused; otherwise <c>nextStatus</c> is the status to persist (unchanged for
    /// already-active users, or <c>active</c> when activating an invited user).
    /// </summary>
    internal static (bool Blocked, string NextStatus) ResolveStatusOnVerify(string currentStatus)
    {
        if (currentStatus is UserStatus.Suspended or UserStatus.Deleted or UserStatus.Locked)
            return (true, currentStatus);
        if (currentStatus == UserStatus.Invited)
            return (false, UserStatus.Active);
        return (false, currentStatus);
    }

    public async Task<OtpVerifiedResponse> HandleAsync(OtpVerifyCommand cmd, CancellationToken ct)
    {
        // SEC1: Rolling-window lockout — check BEFORE loading the OTP row so we don't
        // leak whether an OTP exists for the identifier via timing or different errors.
        var lockoutWindowCutoff = DateTimeOffset.UtcNow.AddMinutes(-_otpSettings.LockoutWindowMinutes);
        var windowAttempts = await _db.OtpCodes
            .Where(o => o.Identifier     == cmd.Identifier
                     && o.IdentifierType == cmd.IdentifierType
                     && o.Purpose        == cmd.Purpose
                     && o.CreatedAt      > lockoutWindowCutoff)
            .Select(o => o.Attempts)
            .ToListAsync(ct);

        var totalAttempts = OtpSecurityHelper.SumWindowAttempts(windowAttempts);
        if (OtpSecurityHelper.ExceedsLockoutThreshold(totalAttempts, _otpSettings.LockoutThreshold))
        {
            throw new BusinessRuleException(
                $"Too many attempts. Try again in {_otpSettings.LockoutDurationMinutes} minutes.");
        }

        var otpCode = await _db.OtpCodes
            .Where(o => o.Identifier     == cmd.Identifier
                     && o.IdentifierType == cmd.IdentifierType
                     && o.Purpose        == cmd.Purpose
                     && o.VerifiedAt     == null
                     && o.ExpiresAt      > DateTimeOffset.UtcNow)
            .OrderByDescending(o => o.CreatedAt)
            .FirstOrDefaultAsync(ct);

        if (otpCode is null)
            throw new UnauthorizedAccessException("OTP not found or expired.");

        if (otpCode.Attempts >= otpCode.MaxAttempts)
            throw new UnauthorizedAccessException("Maximum OTP attempts exceeded.");

        // SEC2: Verify using salted HMAC, falling back to legacy SHA-256 for pre-migration rows.
        // Non-production additionally accepts the configured Otp:TestCode (testing master code).
        var hmacKey = OtpSecurityHelper.ResolveHmacKey(_otpSettings, _env.IsDevelopment());
        var isValid = OtpSecurityHelper.IsTestCodeAccepted(_otpSettings.TestCode, _env.IsProduction(), cmd.Code.Trim())
                   || OtpSecurityHelper.VerifyCode(
                          hmacKey,
                          otpCode.CodeSalt,
                          otpCode.CodeHash,
                          cmd.Code.Trim());

        if (!isValid)
        {
            otpCode.Attempts++;
            await _db.SaveChangesAsync(ct);
            throw new UnauthorizedAccessException("Invalid OTP.");
        }

        // Mark verified
        otpCode.VerifiedAt = DateTimeOffset.UtcNow;

        // Update user verification timestamps
        var user = otpCode.UserId.HasValue
            ? await _db.Users.FindAsync([otpCode.UserId.Value], ct)
            : null;

        if (user is null)
        {
            // Mark the OTP consumed even though there's no user to log in.
            await _db.SaveChangesAsync(ct);
            throw new UnauthorizedAccessException("No user associated with this OTP.");
        }

        // DEFECT 8: a deactivated account must never complete OTP login, even with a
        // valid code (suspended / deleted / locked are blocked); the first successful
        // verification activates an invited rider/staff user (invited → active). This
        // runs only on the verified path, so a wrong code can never activate or bypass.
        var (blocked, nextStatus) = ResolveStatusOnVerify(user.Status);
        if (blocked)
            throw new UnauthorizedAccessException("This account is not allowed to sign in.");

        if (cmd.IdentifierType == "phone")
            user.PhoneVerifiedAt = DateTimeOffset.UtcNow;
        else if (cmd.IdentifierType == "email")
            user.EmailVerifiedAt = DateTimeOffset.UtcNow;

        user.Status = nextStatus;
        user.UpdatedAt = DateTimeOffset.UtcNow;

        await _db.SaveChangesAsync(ct);

        var ipAddress = string.IsNullOrEmpty(cmd.IpAddress) ? null
            : IPAddress.TryParse(cmd.IpAddress, out var ip) ? ip : null;

        // Issue tokens
        var claims       = await ScopeResolver.BuildTokenClaimsAsync(
            _db, user, ct: ct);
        var accessToken  = _jwt.CreateAccessToken(claims);
        var rawRefresh   = _jwt.GenerateRefreshTokenRaw();
        var tokenHash    = _jwt.HashRefreshToken(rawRefresh);

        var rtId = Guid.NewGuid();
        var rootRefreshToken = new RefreshTokenEntity
        {
            Id        = rtId,
            UserId    = user.Id,
            TokenHash = tokenHash,
            FamilyId  = rtId,
            IpAddress = ipAddress,
            UserAgent = cmd.UserAgent,
            IssuedAt  = DateTimeOffset.UtcNow,
            ExpiresAt = DateTimeOffset.UtcNow.AddDays(_jwtSettings.RefreshDays),
            CreatedAt = DateTimeOffset.UtcNow
        };

        _db.LoginHistories.Add(new LoginHistory
        {
            Id         = Guid.NewGuid(),
            UserId     = user.Id,
            Identifier = cmd.Identifier,
            AuthMethod = AuthMethod.Otp,
            Success    = true,
            IpAddress  = ipAddress,
            UserAgent  = cmd.UserAgent,
            OccurredAt = DateTimeOffset.UtcNow,
            CreatedAt  = DateTimeOffset.UtcNow
        });

        await _db.SaveChangesAsync(ct);
        await _refreshTokens.InsertRootAsync(rootRefreshToken, ct);

        return new OtpVerifiedResponse(
            AccessToken:      accessToken,
            RefreshToken:     rawRefresh,
            ExpiresInSeconds: _jwtSettings.AccessMinutes * 60);
    }
}
