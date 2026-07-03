using core.Application.Common.Interfaces;
using core.Application.Identity.AccessControl.Dtos;
using core.Application.Common;
using Wavio.Utilities.CQRS.Abstractions;
using wavio.SharedDataModel.Enums;
using wavio.Utilities.Exceptions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace core.Application.Identity.AccessControl.Commands.SetPersonStatus;

public sealed record SetPersonStatusCommand(Guid UserId, SetPersonStatusRequest Request, Guid? ActorId)
    : ICommand<SetPersonStatusResult?>;

public class SetPersonStatusCommandHandler : ICommandHandler<SetPersonStatusCommand, SetPersonStatusResult?>
{
    private readonly ICoreDbContext _db;
    private readonly IPasswordHasher _hasher;
    private readonly ISettingsMailer _mailer;
    private readonly ILogger<SetPersonStatusCommandHandler> _log;
    public SetPersonStatusCommandHandler(ICoreDbContext db, IPasswordHasher hasher, ISettingsMailer mailer, ILogger<SetPersonStatusCommandHandler> log)
    { _db = db; _hasher = hasher; _mailer = mailer; _log = log; }

    public async Task<SetPersonStatusResult?> HandleAsync(SetPersonStatusCommand cmd, CancellationToken ct)
    {
        var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == cmd.UserId && u.Status != UserStatus.Deleted, ct);
        if (user is null) return null;

        var now = DateTimeOffset.UtcNow;
        string? tempPasswordToEmail = null;
        switch (cmd.Request.Action?.Trim().ToLowerInvariant())
        {
            case "activate":
                // Invited (or locked) accounts have no usable password yet — set the
                // admin-provided temporary one and force a reset on first login.
                var pwd = cmd.Request.Password;
                if (string.IsNullOrWhiteSpace(pwd) || pwd.Length < 8)
                    throw new ValidationException(new Dictionary<string, string[]>
                        { ["password"] = ["A temporary password of at least 8 characters is required to activate this user."] });
                user.PasswordHash       = _hasher.Hash(pwd);
                user.PasswordChangedAt  = now;
                user.MustChangePassword = true;
                user.InvitationToken    = null;
                user.Status             = UserStatus.Active;
                tempPasswordToEmail     = pwd;
                break;

            case "suspend":
                user.Status = UserStatus.Suspended;
                break;

            case "reactivate":
                // Already has a password from a prior activation — just lift the suspension.
                if (user.PasswordHash is null)
                    throw new ValidationException(new Dictionary<string, string[]>
                        { ["action"] = ["This user has never been activated — use Activate to set a password."] });
                user.Status = UserStatus.Active;
                break;

            default:
                throw new ValidationException(new Dictionary<string, string[]>
                    { ["action"] = ["Action must be one of: activate, suspend, reactivate."] });
        }

        user.UpdatedAt = now;
        user.UpdatedBy = cmd.ActorId;
        user.Version++;
        await _db.SaveChangesAsync(ct);

        // Best-effort: email the freshly-activated user their temporary password.
        if (tempPasswordToEmail is not null && !string.IsNullOrWhiteSpace(user.Email))
        {
            try
            {
                // Users carry no direct brand_id (brand comes via membership); resolve
                // the settings brand from the request's RLS scope (null → first brand).
                var baseUrl = (await SettingsStore.LoadAdminBaseUrlAsync(_db, null, ct)).TrimEnd('/');
                var name = await _db.UserProfiles.AsNoTracking().Where(p => p.UserId == user.Id)
                    .Select(p => ((p.FirstName ?? "") + " " + (p.LastName ?? "")).Trim()).FirstOrDefaultAsync(ct);
                var (subject, html) = EmailTemplates.Activated(name ?? "", user.Email!, tempPasswordToEmail, $"{baseUrl}/login");
                await _mailer.SendAsync(null, user.Email!, subject, html, ct);
            }
            catch (Exception ex)
            {
                _log.LogError(ex, "Failed to send activation email to {Email}.", user.Email);
            }
        }

        return new SetPersonStatusResult(user.Status, user.MustChangePassword);
    }
}
