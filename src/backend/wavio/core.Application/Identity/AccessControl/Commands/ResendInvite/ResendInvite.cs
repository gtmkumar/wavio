using System.Security.Cryptography;
using core.Application.Common.Interfaces;
using core.Application.Identity.AccessControl.Commands.InviteUser;
using Wavio.Utilities.CQRS.Abstractions;
using wavio.SharedDataModel.Enums;
using wavio.Utilities.Exceptions;
using wavio.Utilities.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace core.Application.Identity.AccessControl.Commands.ResendInvite;

/// <summary>Re-send the invitation email to a still-pending user. Returns false if the user is not
/// found; throws if the user is no longer 'invited' (already active/suspended).</summary>
public sealed record ResendInviteCommand(Guid UserId) : ICommand<bool>;

public class ResendInviteCommandHandler : ICommandHandler<ResendInviteCommand, bool>
{
    private readonly ICoreDbContext _db;
    private readonly ISettingsMailer _mailer;
    private readonly ICurrentUser _actor;
    private readonly ILogger<ResendInviteCommandHandler> _log;
    public ResendInviteCommandHandler(ICoreDbContext db, ISettingsMailer mailer, ICurrentUser actor, ILogger<ResendInviteCommandHandler> log)
    { _db = db; _mailer = mailer; _actor = actor; _log = log; }

    public async Task<bool> HandleAsync(ResendInviteCommand cmd, CancellationToken ct)
    {
        var user = await _db.Users.Include(u => u.Profile).FirstOrDefaultAsync(u => u.Id == cmd.UserId, ct);
        if (user is null) return false;
        if (user.Status != UserStatus.Invited)
            throw new BusinessRuleException("Only a user who is still 'invited' can have their invitation resent.");

        // Rotate the invitation token (the previous link may be lost or expired) and refresh the
        // sent timestamp, then re-send via the shared invite mailer.
        user.InvitationToken  = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        user.InvitationSentAt = DateTimeOffset.UtcNow;
        user.UpdatedAt        = DateTimeOffset.UtcNow;
        user.UpdatedBy        = _actor.UserId;
        await _db.SaveChangesAsync(ct);

        var name = !string.IsNullOrWhiteSpace(user.Profile?.DisplayName)
            ? user.Profile!.DisplayName!
            : $"{user.Profile?.FirstName} {user.Profile?.LastName}".Trim();
        await InviteEmailSender.SendAsync(_db, _mailer, _log, _actor, user.Id, user.Email, name, ct);
        return true;
    }
}
