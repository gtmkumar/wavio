using core.Application.Common.Interfaces;
using core.Application.Common;
using wavio.Utilities.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace core.Application.Identity.AccessControl.Commands.InviteUser;

/// <summary>
/// Sends the invitation email — a self-service accept-link or an admin-activate notice, per the
/// tenant's provisioning mode. Shared by the initial invite and the resend-invite flow. Best-effort:
/// a mail failure is logged, never thrown, so it can't roll back a successful invite/resend.
/// </summary>
internal static class InviteEmailSender
{
    public static async Task SendAsync(
        ICoreDbContext db, ISettingsMailer mailer, ILogger log,
        ICurrentUser actor, Guid userId, string? email, string name, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(email)) return;
        try
        {
            var mode = await SettingsStore.LoadProvisioningModeAsync(db, actor.TenantId, ct);
            if (mode == "self_service")
            {
                var token = await db.Users.AsNoTracking().Where(u => u.Id == userId)
                    .Select(u => u.InvitationToken).FirstOrDefaultAsync(ct);
                if (string.IsNullOrEmpty(token))
                {
                    log.LogWarning("Invited user {UserId} has no invitation token; skipping self-service email.", userId);
                    return;
                }
                var baseUrl = (await SettingsStore.LoadAdminBaseUrlAsync(db, actor.TenantId, ct)).TrimEnd('/');
                var acceptUrl = $"{baseUrl}/accept-invite?token={Uri.EscapeDataString(token)}";
                var (subject, html) = EmailTemplates.InviteSelfService(name, acceptUrl);
                await mailer.SendAsync(actor.TenantId, email, subject, html, ct);
            }
            else
            {
                var (subject, html) = EmailTemplates.InviteAdminActivate(name);
                await mailer.SendAsync(actor.TenantId, email, subject, html, ct);
            }
        }
        catch (Exception ex)
        {
            log.LogError(ex, "Failed to send invite email to {Email}.", email);
        }
    }
}
