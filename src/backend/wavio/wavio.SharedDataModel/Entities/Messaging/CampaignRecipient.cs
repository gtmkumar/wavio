namespace wavio.SharedDataModel.Entities.Messaging;

/// <summary>
/// Per-recipient fan-out state for a <see cref="Campaign"/> (messaging.campaign_recipients, issue
/// #22, db/migrations/V013__campaigns.sql). Suppressed recipients are marked here up-front, at
/// campaign creation (spec §4.10 deny-wins); the chunker (WaGateway.Infrastructure's
/// <c>CampaignChunkerService</c>) only ever claims 'pending' rows, so a broadcast larger than the
/// current tier headroom resumes across days as headroom frees up.
/// </summary>
public class CampaignRecipient
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid CampaignId { get; set; }

    /// <summary>Recipient WhatsApp id — PII, mask in logs.</summary>
    public string WaId { get; set; } = null!;

    /// <summary>Per-recipient template variable/component values (jsonb), overriding
    /// <see cref="Campaign.Params"/> when present — see that property's doc comment.</summary>
    public string? Params { get; set; }

    /// <summary>pending -&gt; sent (outbound accepted; <see cref="OutboundMessageId"/> set) -&gt;
    /// delivered -&gt; read, or suppressed (marked at launch, never dispatched), failed (dispatch
    /// or delivery failure; <see cref="ErrorCode"/> set), cancelled (campaign cancelled, or its
    /// pinned template was DISABLED, before dispatch). Delivery-state transitions mirror the
    /// linked outbound_message via <c>CampaignStatusConsumerService</c> consuming
    /// <c>wa.message.status.v1</c>.</summary>
    public string Status { get; set; } = "pending";

    public Guid? OutboundMessageId { get; set; }
    public string? ErrorCode { get; set; }
    public DateTimeOffset? ProcessedAt { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public Guid? CreatedBy { get; set; }
    public Guid? UpdatedBy { get; set; }
    public int Version { get; set; }
}
