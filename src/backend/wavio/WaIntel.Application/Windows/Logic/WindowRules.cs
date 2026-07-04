namespace WaIntel.Application.Windows.Logic;

/// <summary>
/// Pure window-state-machine rules (issue #15, spec §2.2/§4.5) — no DB, no I/O, so the CS/CTWA
/// transition and expiry-boundary rules can be unit tested directly without a database.
/// </summary>
public static class WindowRules
{
    /// <summary>Customer-service window: last inbound + 24h, reset on every consumer message.</summary>
    public static readonly TimeSpan CsWindowDuration = TimeSpan.FromHours(24);

    /// <summary>Click-to-WhatsApp window: referral entry + 72h.</summary>
    public static readonly TimeSpan CtwaWindowDuration = TimeSpan.FromHours(72);

    /// <summary>How far before expiry <c>wa.window.closing</c> is emitted (spec §4.5).</summary>
    public static readonly TimeSpan DefaultClosingHorizon = TimeSpan.FromHours(2);

    /// <summary>CS window expiry for a consumer message sent at <paramref name="sentAt"/>.</summary>
    public static DateTimeOffset CalculateCsExpiry(DateTimeOffset sentAt) => sentAt + CsWindowDuration;

    /// <summary>CTWA window expiry for a referral entry at <paramref name="enteredAt"/>.</summary>
    public static DateTimeOffset CalculateCtwaExpiry(DateTimeOffset enteredAt) => enteredAt + CtwaWindowDuration;

    /// <summary>A window is open iff it has an expiry and that expiry is strictly in the future.</summary>
    public static bool IsOpen(DateTimeOffset? expiresAt, DateTimeOffset now) =>
        expiresAt.HasValue && expiresAt.Value > now;

    /// <summary>
    /// True when a window is expiring within <paramref name="horizon"/> of <paramref name="now"/>
    /// (inclusive of "already past due but not yet marked notified" — a scan that runs late
    /// should still catch it) AND hasn't already been notified for its CURRENT expiry.
    /// <paramref name="closingNotifiedAt"/> is compared against nothing else — the caller is
    /// responsible for resetting it to null whenever the expiry is extended (that's what makes a
    /// re-opened window eligible for a fresh notification; see db/migrations/V008 column comment).
    /// </summary>
    public static bool IsApproachingClose(
        DateTimeOffset? expiresAt, DateTimeOffset? closingNotifiedAt, DateTimeOffset now, TimeSpan horizon)
    {
        if (!expiresAt.HasValue) return false;
        if (closingNotifiedAt.HasValue) return false; // already notified for this expiry — guard against double-emit
        return expiresAt.Value <= now + horizon;
    }
}
