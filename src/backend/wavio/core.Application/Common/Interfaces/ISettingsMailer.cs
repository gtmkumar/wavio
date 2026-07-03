using core.Application.Common;

namespace core.Application.Common.Interfaces;

/// <summary>
/// Sends transactional email using the SMTP transport configured in
/// <c>kernel.system_settings</c> (tenant-scoped). All sends are best-effort:
/// callers in the invite / activation flows must not fail their operation when
/// mail can't be delivered.
/// </summary>
public interface ISettingsMailer
{
    /// <summary>Loads the persisted SMTP config for a tenant (null = first/only tenant under RLS bypass).</summary>
    Task<EmailSettings?> LoadAsync(Guid? tenantId, CancellationToken ct = default);

    /// <summary>Best-effort send using the persisted config. Returns false (and logs) if unconfigured/disabled/failed.</summary>
    Task<bool> SendAsync(Guid? tenantId, string to, string subject, string htmlBody, CancellationToken ct = default);

    /// <summary>Sends using an explicit config (possibly unsaved) and surfaces the error — used by "Send test email".</summary>
    Task<(bool ok, string? error)> TestAsync(EmailSettings cfg, string to, CancellationToken ct = default);
}
