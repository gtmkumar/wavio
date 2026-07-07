namespace wavio.SharedDataModel.Entities.Waba;

/// <summary>
/// waba.business_accounts (db/migrations/V002__waba.sql + V014 onboarding columns) — one row per
/// Meta WhatsApp Business Account. Created by the onboarding wizard's Embedded Signup handler
/// (docs/ONBOARDING_WIZARD_PLAN.md); before that, rows were dev fixtures only.
/// <see cref="SystemUserTokenCiphertext"/> holds the per-WABA business token envelope-encrypted
/// at the app layer via <c>IFieldCipher</c> (spec §5) — never plaintext, never logged.
/// </summary>
public class WabaBusinessAccount
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public string MetaWabaId { get; set; } = null!;
    public string Name { get; set; } = null!;
    public string? CurrencyCode { get; set; }
    public string? MessageTemplateNamespace { get; set; }

    /// <summary>IFieldCipher output ("enc:v1:..." prefixed). Decrypt only at the point of a
    /// Graph API call; never return it from any endpoint.</summary>
    public string? SystemUserTokenCiphertext { get; set; }

    /// <summary>Which key encrypted the token (key-rotation bookkeeping, spec §5).</summary>
    public string? TokenKeyRef { get; set; }

    public string Status { get; set; } = null!;

    /// <summary>Meta business-verification review state (V014) — mirrored from Graph,
    /// e.g. pending/verified/not_verified. Meta owns the review; we only display it.</summary>
    public string? VerificationStatus { get; set; }

    /// <summary>When subscribed_apps last succeeded for this WABA (V014). Null = webhooks
    /// not subscribed yet.</summary>
    public DateTimeOffset? WebhooksSubscribedAt { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public Guid? CreatedBy { get; set; }
    public Guid? UpdatedBy { get; set; }
    public int Version { get; set; }
}
