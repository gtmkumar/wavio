using System.Text.Json;
using core.Application.Common;
using core.Application.Common.Interfaces;
using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using MimeKit;

namespace core.Infrastructure.Email;

/// <summary>
/// Sends transactional email using the SMTP transport configured in
/// <c>kernel.system_settings</c> (tenant-scoped). All sends are best-effort:
/// callers in the invite / activation flows must not fail their operation when
/// mail can't be delivered.
/// </summary>
public sealed class SettingsMailer : ISettingsMailer
{
    private readonly ICoreDbContext _db;
    private readonly ILogger<SettingsMailer> _log;

    public SettingsMailer(ICoreDbContext db, ILogger<SettingsMailer> log)
    {
        _db = db;
        _log = log;
    }

    public async Task<EmailSettings?> LoadAsync(Guid? tenantId, CancellationToken ct = default)
    {
        // The RLS connection interceptor already scopes this query to the request's
        // tenant (or all tenants for a platform admin via bypass), so an unfiltered
        // category/key lookup returns the correct row.
        var query = _db.SystemSettings.AsNoTracking()
            .Where(s => s.Category == "email" && s.SettingKey == "smtp" && s.Status == "active");
        if (tenantId.HasValue)
            query = query.Where(s => s.TenantId == tenantId);

        var row = await query.OrderBy(s => s.TenantId == null).FirstOrDefaultAsync(ct);
        if (row is null) return null;

        try
        {
            return JsonSerializer.Deserialize<EmailSettings>(row.SettingValue);
        }
        catch (JsonException ex)
        {
            _log.LogWarning(ex, "email/smtp setting JSON is malformed");
            return null;
        }
    }

    public async Task<bool> SendAsync(Guid? tenantId, string to, string subject, string htmlBody, CancellationToken ct = default)
    {
        var cfg = await LoadAsync(tenantId, ct);
        if (cfg is null || !cfg.Enabled || !cfg.IsConfigured)
        {
            _log.LogInformation("Skipping email to {To} ({Subject}) — SMTP not enabled/configured.", to, subject);
            return false;
        }

        try
        {
            await DeliverAsync(cfg, to, subject, htmlBody, ct);
            _log.LogInformation("Sent email to {To} ({Subject}).", to, subject);
            return true;
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Failed to send email to {To} ({Subject}).", to, subject);
            return false;
        }
    }

    public async Task<(bool ok, string? error)> TestAsync(EmailSettings cfg, string to, CancellationToken ct = default)
    {
        if (!cfg.IsConfigured)
            return (false, "Host and From address are required.");
        try
        {
            await DeliverAsync(cfg, to,
                "Wavio — SMTP test",
                "<p>This is a test email from your Wavio admin console.</p>" +
                "<p>If you received this, outbound email is configured correctly. ✅</p>", ct);
            return (true, null);
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "SMTP test send failed");
            return (false, ex.Message);
        }
    }

    private static async Task DeliverAsync(EmailSettings cfg, string to, string subject, string htmlBody, CancellationToken ct)
    {
        var msg = new MimeMessage();
        msg.From.Add(new MailboxAddress(cfg.FromName ?? "Wavio", cfg.FromEmail));
        msg.To.Add(MailboxAddress.Parse(to));
        msg.Subject = subject;
        msg.Body = new BodyBuilder { HtmlBody = htmlBody }.ToMessageBody();

        // Port 465 → implicit TLS; 587/other with secure → STARTTLS; otherwise plain.
        var socket = cfg.Secure
            ? (cfg.Port == 465 ? SecureSocketOptions.SslOnConnect : SecureSocketOptions.StartTls)
            : SecureSocketOptions.None;

        using var smtp = new SmtpClient();
        await smtp.ConnectAsync(cfg.Host, cfg.Port, socket, ct);
        if (!string.IsNullOrEmpty(cfg.Username))
            await smtp.AuthenticateAsync(cfg.Username, cfg.Password, ct);
        await smtp.SendAsync(msg, ct);
        await smtp.DisconnectAsync(quit: true, ct);
    }
}
