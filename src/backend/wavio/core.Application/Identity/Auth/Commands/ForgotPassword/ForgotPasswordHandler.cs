using System.Security.Cryptography;
using core.Application.Common.Interfaces;
using Wavio.Utilities.CQRS.Abstractions;
using wavio.SharedDataModel.Entities.IdentityAccess;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace core.Application.Identity.Auth.Commands.ForgotPassword;

/// <summary>
/// Creates a single-use password reset token (SHA-256 hashed) with a 1-hour TTL.
/// C4: Raw token is only logged in Development; in other environments the log is suppressed
/// so prod can't silently emit reset tokens.
/// </summary>
public sealed class ForgotPasswordHandler : ICommandHandler<ForgotPasswordCommand, bool>
{
    private const int TokenBytes = 32;
    private const int TtlMinutes = 60;

    private readonly ICoreDbContext   _db;
    private readonly IHostEnvironment _env;
    private readonly ILogger<ForgotPasswordHandler> _logger;

    public ForgotPasswordHandler(
        ICoreDbContext db,
        IHostEnvironment env,
        ILogger<ForgotPasswordHandler> logger)
    {
        _db     = db;
        _env    = env;
        _logger = logger;
    }

    public async Task<bool> HandleAsync(ForgotPasswordCommand cmd, CancellationToken ct)
    {
        var user = await _db.Users
            .Where(u => u.Email == cmd.Identifier || u.PhoneE164 == cmd.Identifier)
            .FirstOrDefaultAsync(ct);

        // Always return success — no user enumeration
        if (user is null) return true;

        // Expire any existing active reset tokens
        var existing = await _db.PasswordResets
            .Where(r => r.UserId == user.Id && r.UsedAt == null && r.ExpiresAt > DateTimeOffset.UtcNow)
            .ToListAsync(ct);

        foreach (var old in existing)
            old.ExpiresAt = DateTimeOffset.UtcNow.AddSeconds(-1);

        var rawToken  = Convert.ToBase64String(RandomNumberGenerator.GetBytes(TokenBytes));
        var tokenHash = HashToken(rawToken);

        _db.PasswordResets.Add(new PasswordReset
        {
            Id        = Guid.NewGuid(),
            UserId    = user.Id,
            TokenHash = tokenHash,
            ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(TtlMinutes),
            Status    = "active",
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        });

        await _db.SaveChangesAsync(ct);

        // C4: Only log the raw token in Development. In all other environments the token
        // must be delivered via email/SMS — not emitted to logs that may be aggregated.
        if (_env.IsDevelopment())
        {
            _logger.LogWarning("[DEV-RESET] userId={UserId} token={Token}", user.Id, rawToken);
        }
        else
        {
            // TODO: integrate email/SMS dispatch before going live.
            _logger.LogInformation("[RESET] Password reset requested for userId={UserId}.", user.Id);
        }

        return true;
    }

    internal static string HashToken(string raw)
    {
        var bytes = SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(raw));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }
}
