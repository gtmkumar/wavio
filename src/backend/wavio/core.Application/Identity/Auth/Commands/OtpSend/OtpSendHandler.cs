using System.Net;
using core.Application.Common.Interfaces;
using core.Application.Identity.Auth.Common;
using core.Application.Identity.Auth.Dtos;
using Wavio.Utilities.CQRS.Abstractions;
using wavio.SharedDataModel.Entities.IdentityAccess;
using wavio.Utilities.Exceptions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace core.Application.Identity.Auth.Commands.OtpSend;

/// <summary>
/// Generates a 6-digit OTP, stores its HMAC-SHA256 hash (+per-row salt) in otp_codes,
/// then dispatches via IOtpSender.
///
/// Security properties enforced:
///   L6  — TTL / MaxAttempts from IOptions&lt;OtpSettings&gt;.
///   C5  — Per-identifier cooldown before issuing a new OTP.
///   SEC1 — Rolling-window lockout: ≥ LockoutThreshold failed verifies across any rows
///           for the same identifier in the last LockoutWindowMinutes → block send for
///           LockoutDurationMinutes.  Covers resend-cycling attacks.
///   SEC2 — HMAC-SHA256 with per-row random salt (replaces unsalted SHA-256).
/// </summary>
public sealed class OtpSendHandler : ICommandHandler<OtpSendCommand, OtpSentResponse>
{
    private const int CodeLength = 6;

    private readonly ICoreDbContext   _db;
    private readonly IOtpSender       _sender;
    private readonly OtpSettings      _settings;
    private readonly IHostEnvironment _env;
    private readonly ILogger<OtpSendHandler> _logger;

    public OtpSendHandler(
        ICoreDbContext db,
        IOtpSender sender,
        IOptions<OtpSettings> otpOptions,
        IHostEnvironment env,
        ILogger<OtpSendHandler> logger)
    {
        _db       = db;
        _sender   = sender;
        _settings = otpOptions.Value;
        _env      = env;
        _logger   = logger;
    }

    public async Task<OtpSentResponse> HandleAsync(OtpSendCommand cmd, CancellationToken ct)
    {
        // SEC1: Rolling-window lockout — block send if the identifier has accumulated
        // >= LockoutThreshold failed verifies within the last LockoutWindowMinutes.
        // We sum Attempts on ALL rows (verified or expired) within the window so that
        // resend-cycling (issuing a fresh row with Attempts=0) does not bypass the budget.
        var lockoutWindowCutoff = DateTimeOffset.UtcNow.AddMinutes(-_settings.LockoutWindowMinutes);
        var windowAttempts = await _db.OtpCodes
            .Where(o => o.Identifier     == cmd.Identifier
                     && o.IdentifierType == cmd.IdentifierType
                     && o.Purpose        == cmd.Purpose
                     && o.CreatedAt      > lockoutWindowCutoff)
            .Select(o => o.Attempts)
            .ToListAsync(ct);

        var totalAttempts = OtpSecurityHelper.SumWindowAttempts(windowAttempts);
        if (OtpSecurityHelper.ExceedsLockoutThreshold(totalAttempts, _settings.LockoutThreshold))
        {
            // Do NOT reveal whether the identifier exists; use a generic message.
            throw new BusinessRuleException(
                $"Too many attempts. Try again in {_settings.LockoutDurationMinutes} minutes.");
        }

        // C5: Per-identifier cooldown — reject if an OTP was issued within the cooldown window
        var cooldownCutoff = DateTimeOffset.UtcNow.AddSeconds(-_settings.ResendCooldownSeconds);
        var recentOtp = await _db.OtpCodes
            .Where(o => o.Identifier     == cmd.Identifier
                     && o.IdentifierType == cmd.IdentifierType
                     && o.Purpose        == cmd.Purpose
                     && o.VerifiedAt     == null
                     && o.CreatedAt      > cooldownCutoff)
            .OrderByDescending(o => o.CreatedAt)
            .FirstOrDefaultAsync(ct);

        if (recentOtp is not null)
        {
            var waitSeconds = (int)Math.Ceiling(
                (recentOtp.CreatedAt.AddSeconds(_settings.ResendCooldownSeconds) - DateTimeOffset.UtcNow).TotalSeconds);
            throw new ValidationException(
                new Dictionary<string, string[]>
                {
                    ["identifier"] = [$"Please wait {waitSeconds} seconds before requesting a new OTP."]
                });
        }

        // Invalidate any existing un-verified OTPs for this identifier+purpose
        var existing = await _db.OtpCodes
            .Where(o => o.Identifier     == cmd.Identifier
                     && o.Purpose        == cmd.Purpose
                     && o.IdentifierType == cmd.IdentifierType
                     && o.VerifiedAt     == null
                     && o.ExpiresAt      > DateTimeOffset.UtcNow)
            .ToListAsync(ct);

        foreach (var old in existing)
            old.ExpiresAt = DateTimeOffset.UtcNow.AddSeconds(-1);

        // SEC2: Generate OTP with HMAC-SHA256 + per-row random salt
        var plainCode = GenerateNumericCode(CodeLength);
        var salt      = OtpSecurityHelper.GenerateSalt();
        var hmacKey   = OtpSecurityHelper.ResolveHmacKey(_settings, _env.IsDevelopment());
        var codeHash  = OtpSecurityHelper.ComputeHmac(hmacKey, salt, plainCode);
        var expiresAt = DateTimeOffset.UtcNow.AddMinutes(_settings.TtlMinutes);

        var ipAddress = string.IsNullOrEmpty(cmd.IpAddress) ? null
            : IPAddress.TryParse(cmd.IpAddress, out var ip) ? ip : null;

        var userId = await _db.Users
            .Where(u => u.Email == cmd.Identifier || u.PhoneE164 == cmd.Identifier)
            .Select(u => (Guid?)u.Id)
            .FirstOrDefaultAsync(ct);

        _db.OtpCodes.Add(new OtpCode
        {
            Id             = Guid.NewGuid(),
            Purpose        = cmd.Purpose,
            Identifier     = cmd.Identifier,
            IdentifierType = cmd.IdentifierType,
            CodeHash       = codeHash,
            CodeSalt       = salt,
            UserId         = userId,
            Attempts       = 0,
            MaxAttempts    = (short)_settings.MaxAttempts,
            ExpiresAt      = expiresAt,
            IpAddress      = ipAddress,
            UserAgent      = cmd.UserAgent,
            CreatedAt      = DateTimeOffset.UtcNow
        });

        await _db.SaveChangesAsync(ct);

        await _sender.SendAsync(cmd.Identifier, cmd.IdentifierType, plainCode, cmd.Purpose, ct);

        return new OtpSentResponse(
            Message:   "OTP sent successfully.",
            ExpiresAt: expiresAt);
    }

    private static string GenerateNumericCode(int length)
    {
        var max    = (int)Math.Pow(10, length);
        var random = System.Security.Cryptography.RandomNumberGenerator.GetInt32(max);
        return random.ToString().PadLeft(length, '0');
    }
}
