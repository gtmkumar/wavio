using core.Application.Common;
using core.Application.Common.Interfaces;
using core.Application.Identity.Auth.Common;
using core.Application.Identity.Auth.Dtos;
using Wavio.Utilities.CQRS.Abstractions;
using wavio.SharedDataModel.Enums;
using wavio.Utilities.Auth;
using wavio.Utilities.Exceptions;
using wavio.Utilities.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace core.Application.Identity.Auth.Commands.StepUpVerify;

/// <summary>
/// Step-up (§8) verification for an already-authenticated system user. Reuses the exact OTP crypto
/// + rolling-window lockout as OtpVerifyHandler (SEC1/SEC2) for purpose=sensitive_action, but on
/// success it re-mints ONLY the access token — preserving the caller's active scope and adding a
/// fresh <c>amr</c>+<c>stepup_at</c> proof. It does NOT rotate refresh tokens or write a login row:
/// it is a re-verification, not a login. The identifier is taken from the caller's own token, so a
/// user can only step up their own account.
/// </summary>
public sealed class StepUpVerifyHandler : ICommandHandler<StepUpVerifyCommand, StepUpVerifiedResponse>
{
    private readonly ICoreDbContext   _db;
    private readonly IJwtTokenService _jwt;
    private readonly ICurrentUser     _current;
    private readonly JwtSettings      _jwtSettings;
    private readonly OtpSettings       _otpSettings;
    private readonly IHostEnvironment  _env;
    private readonly IConfiguration    _config;

    public StepUpVerifyHandler(
        ICoreDbContext db,
        IJwtTokenService jwt,
        ICurrentUser current,
        IOptions<JwtSettings> jwtOptions,
        IOptions<OtpSettings> otpOptions,
        IHostEnvironment env,
        IConfiguration config)
    {
        _db          = db;
        _jwt         = jwt;
        _current     = current;
        _jwtSettings = jwtOptions.Value;
        _otpSettings = otpOptions.Value;
        _env         = env;
        _config      = config;
    }

    public async Task<StepUpVerifiedResponse> HandleAsync(StepUpVerifyCommand cmd, CancellationToken ct)
    {
        var userId = _current.UserId ?? throw new ForbiddenException("Authentication required.");
        var identifierType = cmd.Request.IdentifierType;

        // Identifier is the caller's OWN contact from the validated token — never client-supplied.
        var identifier = identifierType == "email" ? _current.Email : _current.Phone;
        if (string.IsNullOrEmpty(identifier))
            throw new BusinessRuleException($"No {identifierType} on file to verify against.");

        // SEC1: rolling-window lockout BEFORE loading the row (no existence oracle).
        var lockoutWindowCutoff = DateTimeOffset.UtcNow.AddMinutes(-_otpSettings.LockoutWindowMinutes);
        var windowAttempts = await _db.OtpCodes
            .Where(o => o.Identifier     == identifier
                     && o.IdentifierType == identifierType
                     && o.Purpose        == OtpPurpose.SensitiveAction
                     && o.CreatedAt      > lockoutWindowCutoff)
            .Select(o => o.Attempts)
            .ToListAsync(ct);

        if (OtpSecurityHelper.ExceedsLockoutThreshold(
                OtpSecurityHelper.SumWindowAttempts(windowAttempts), _otpSettings.LockoutThreshold))
        {
            throw new BusinessRuleException(
                $"Too many attempts. Try again in {_otpSettings.LockoutDurationMinutes} minutes.");
        }

        var otpCode = await _db.OtpCodes
            .Where(o => o.Identifier     == identifier
                     && o.IdentifierType == identifierType
                     && o.Purpose        == OtpPurpose.SensitiveAction
                     && o.VerifiedAt     == null
                     && o.ExpiresAt      > DateTimeOffset.UtcNow)
            .OrderByDescending(o => o.CreatedAt)
            .FirstOrDefaultAsync(ct);

        if (otpCode is null)
            throw new UnauthorizedAccessException("OTP not found or expired.");
        if (otpCode.Attempts >= otpCode.MaxAttempts)
            throw new UnauthorizedAccessException("Maximum OTP attempts exceeded.");

        // SEC2: salted HMAC verify (test master code accepted only outside production).
        var hmacKey = OtpSecurityHelper.ResolveHmacKey(_otpSettings, _env.IsDevelopment());
        var isValid = OtpSecurityHelper.IsTestCodeAccepted(_otpSettings.TestCode, _env.IsProduction(), cmd.Request.Code.Trim())
                   || OtpSecurityHelper.VerifyCode(hmacKey, otpCode.CodeSalt, otpCode.CodeHash, cmd.Request.Code.Trim());

        if (!isValid)
        {
            otpCode.Attempts++;
            await _db.SaveChangesAsync(ct);
            throw new UnauthorizedAccessException("Invalid OTP.");
        }

        otpCode.VerifiedAt = DateTimeOffset.UtcNow;
        await _db.SaveChangesAsync(ct);

        var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == userId && u.DeletedAt == null, ct)
            ?? throw new ForbiddenException("User not found.");

        // Re-mint the access token PRESERVING the caller's currently-active scope, then stamp the
        // fresh step-up proof. No refresh rotation, no LoginHistory — this is not a login.
        var baseClaims = await ScopeResolver.BuildTokenClaimsAsync(
            _db, user, _current.ScopeType, _current.ScopeId, ct: ct);

        var upgraded = baseClaims with
        {
            Amr      = AuthMethod.Otp,
            StepUpAt = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
        };

        var accessToken = _jwt.CreateAccessToken(upgraded);
        return new StepUpVerifiedResponse(accessToken, _jwtSettings.AccessMinutes * 60);
    }
}
