namespace wavio.SharedDataModel.Entities.Sessions;

/// <summary>
/// One row per (tenant_id, phone_number_id, user_wa_id) pair — the free-messaging window state
/// for that customer conversation (sessions.conversation_windows, issue #15,
/// db/migrations/V008__sessions.sql). Maintained by UPSERT onto the unique key
/// (TenantId, PhoneNumberId, UserWaId); there is no history of past windows here (see
/// <see cref="WindowEvent"/> for that) — this row always reflects the CURRENT window state.
///
/// A window is open iff its expiry is in the future:
///   CsExpiresAt   — customer-service window: last inbound + 24h, reset on every consumer message.
///   CtwaExpiresAt — click-to-WhatsApp window: referral entry + 72h, set when a message carries
///                   Meta's referral object (spec §2.2, §4.5).
/// </summary>
public class ConversationWindow
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid PhoneNumberId { get; set; }

    /// <summary>Customer WhatsApp id — PII, mask in logs.</summary>
    public string UserWaId { get; set; } = null!;

    /// <summary>organic | ctwa | fb_cta (CHECK-enforced in the DB).</summary>
    public string Origin { get; set; } = "organic";

    public DateTimeOffset? CsExpiresAt { get; set; }
    public DateTimeOffset? CsLastInboundAt { get; set; }

    public DateTimeOffset? CtwaExpiresAt { get; set; }
    public DateTimeOffset? CtwaEnteredAt { get; set; }

    /// <summary>Raw Meta referral object captured at CTWA entry (jsonb) — diagnostic/audit only.</summary>
    public string? CtwaReferral { get; set; }

    /// <summary>
    /// When wa.window.closing was emitted for the CURRENT window. Reset to null whenever
    /// CsExpiresAt/CtwaExpiresAt is extended, so a re-opened window gets a fresh notification
    /// (db/migrations/V008 column comment) — this is the double-emit guard.
    /// </summary>
    public DateTimeOffset? ClosingNotifiedAt { get; set; }

    /// <summary>True only for rows fabricated by the non-prod simulation endpoint. Must never be
    /// true in production (DB column comment) — the simulation endpoint itself is hard-gated on
    /// environment, this flag is the row-level record of that.</summary>
    public bool IsSimulated { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public Guid? CreatedBy { get; set; }
    public Guid? UpdatedBy { get; set; }
    public int Version { get; set; }
}
