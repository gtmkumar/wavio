namespace WaPlatform.Contracts.IntegrationEvents.V1;

/// <summary>
/// <c>wa.message.received.v1</c> — an inbound customer message was ingested, deduped on
/// <paramref name="Wamid"/>, and normalized by wa-ingest-svc (spec §4.3).
/// </summary>
public sealed record MessageReceivedV1 : IntegrationEvent
{
    public const string Name = "wa.message.received.v1";
    public override string EventName => Name;

    /// <summary>WhatsApp message id — also the correlation id for the whole chain (spec §3.2).</summary>
    public required string Wamid { get; init; }

    /// <summary>Customer WhatsApp id (PII — mask in logs).</summary>
    public required string WaId { get; init; }

    /// <summary>Meta phone_number_id the message arrived on.</summary>
    public required string PhoneNumberId { get; init; }

    /// <summary>WABA the phone number belongs to.</summary>
    public required string WabaId { get; init; }

    /// <summary>Meta message type: text, image, interactive, button, order, …</summary>
    public required string MessageType { get; init; }

    /// <summary>Meta-reported send timestamp of the customer message.</summary>
    public required DateTimeOffset SentAt { get; init; }

    /// <summary>True when the message opened/refreshed a customer-service window.</summary>
    public bool OpensCustomerServiceWindow { get; init; } = true;

    /// <summary>
    /// Meta's raw <c>referral</c> object (serialized JSON), present only when this message
    /// arrived via a Click-to-WhatsApp ad or Facebook Page CTA (spec §2.2, §4.5 — this is what
    /// opens/extends the 72h CTWA window). Null for an ordinary organic message.
    ///
    /// Additive field (contract rule: additive-only within v1) — added by issue #15 (Session
    /// Window Manager). wa-ingest-svc's normalizer does NOT populate this yet as of issue #13;
    /// that's a real gap, not an oversight — see
    /// .claude/agent-memory/dotnet-backend-developer/issue-15-session-windows.md for why it
    /// wasn't fixed as part of this issue (wa-ingest-svc's PR was actively receiving commits from
    /// another agent) and what populating it requires.
    /// </summary>
    public string? Referral { get; init; }

    /// <summary>
    /// The message body text — PII, do not log verbatim. Additive field (issue #21: the
    /// STOP-keyword listener needs to inspect inbound text for opt-out phrases, and no prior
    /// consumer of this event ever needed the body). Populated only when
    /// <see cref="MessageType"/> is <c>text</c> (Meta's <c>text.body</c> field); null for every
    /// other message type — this is deliberately narrow rather than also decoding button/list
    /// reply titles or interactive payloads, since spec §4.10's STOP vocabulary is plain-text
    /// keywords, not a structured reply.
    /// </summary>
    public string? Text { get; init; }
}

/// <summary>
/// <c>wa.message.status.v1</c> — delivery status of an outbound message changed
/// (sent → delivered → read, or failed). Carries the webhook <c>pricing</c> object fields,
/// which are the billing source of truth (ADR-002).
/// </summary>
public sealed record MessageStatusV1 : IntegrationEvent
{
    public const string Name = "wa.message.status.v1";
    public override string EventName => Name;

    public required string Wamid { get; init; }
    public required string PhoneNumberId { get; init; }

    /// <summary>sent | delivered | read | failed | deleted.</summary>
    public required string Status { get; init; }

    /// <summary>Meta error code when <see cref="Status"/> is failed.</summary>
    public int? ErrorCode { get; init; }

    /// <summary>Whether Meta marked the message billable (webhook pricing object).</summary>
    public bool? Billable { get; init; }

    /// <summary>Pricing category: marketing | utility | authentication | service | referral_conversion.</summary>
    public string? PricingCategory { get; init; }

    /// <summary>Pricing model reported by Meta (PMP going forward).</summary>
    public string? PricingModel { get; init; }

    /// <summary>
    /// Per-message price Meta reported on this status webhook (PMP), if present. Additive field
    /// (issue #19) — the cost ledger (WaBilling) writes this verbatim; it never derives an amount
    /// from its own rate cards (ADR-002 — rate cards are estimator-only). Null when Meta's payload
    /// doesn't carry a price on this particular status update (e.g. a "sent"/"read" event with no
    /// pricing object at all, or a pre-PMP CBP-era payload).
    /// </summary>
    public decimal? Amount { get; init; }

    /// <summary>ISO 4217 currency code for <see cref="Amount"/>, when present.</summary>
    public string? Currency { get; init; }

    /// <summary>
    /// Recipient's destination market/country as reported by Meta's pricing object, when present.
    /// Additive field (issue #19). Null when Meta doesn't supply it on this webhook — the
    /// estimator (pre-send) takes an explicit country instead of relying on this.
    /// </summary>
    public string? DestinationMarket { get; init; }

    /// <summary>
    /// The raw Meta status-webhook <c>pricing</c> sub-object, verbatim as JSON text. Additive
    /// field (issue #19) — this is what <c>billing.message_costs.webhook_pricing</c> stores;
    /// the typed fields above are a convenience projection of the same object, never the reverse.
    /// Null when the webhook carried no pricing object.
    /// </summary>
    public string? PricingRawJson { get; init; }
}

/// <summary>
/// <c>wa.message.send_failed.v1</c> — an outbound send permanently failed (issue #14): either
/// the Meta Graph API returned a non-retryable error (131026 not-on-WhatsApp, 131047
/// re-engagement required, 131049 per-user marketing limit, or in-process retries exhausted),
/// or a pre-dispatch policy rejected the send before it ever reached the outbox (WINDOW_CLOSED,
/// suppression list, messaging-tier headroom exhausted — spec §4.2, ADR-005).
///
/// Deliberately a NEW event rather than reusing <see cref="MessageStatusV1"/>: that event's
/// <c>Wamid</c> is required, but a Graph rejection or a pre-dispatch rejection both happen
/// BEFORE Meta ever assigns a wamid, so there is nothing to put there. This carries the
/// internal <see cref="OutboundMessageId"/> as the correlation id instead.
/// </summary>
public sealed record MessageSendFailedV1 : IntegrationEvent
{
    public const string Name = "wa.message.send_failed.v1";
    public override string EventName => Name;

    /// <summary>The gateway's own outbound_messages.id — the correlation id for a send that
    /// never got as far as a wamid.</summary>
    public required Guid OutboundMessageId { get; init; }

    public required string PhoneNumberId { get; init; }

    /// <summary>Recipient WhatsApp id (PII — mask in logs).</summary>
    public required string ToWaId { get; init; }

    public required string MessageType { get; init; }

    /// <summary>Meta numeric error code (as a string, e.g. "131026") for a Graph rejection, or
    /// one of our own pre-dispatch reason codes: WINDOW_CLOSED | SUPPRESSED | TIER_EXHAUSTED |
    /// RETRIES_EXHAUSTED.</summary>
    public required string ErrorCode { get; init; }

    public string? ErrorMessage { get; init; }
}

/// <summary>
/// <c>wa.window.closing.v1</c> — a customer-service window is approaching expiry
/// (published by the Session Window Manager, spec §4.5) so verticals can prompt
/// agents / schedule template follow-ups before free-form sends start being rejected.
/// </summary>
public sealed record WindowClosingV1 : IntegrationEvent
{
    public const string Name = "wa.window.closing.v1";
    public override string EventName => Name;

    /// <summary>Customer WhatsApp id (PII — mask in logs).</summary>
    public required string WaId { get; init; }

    public required string PhoneNumberId { get; init; }

    /// <summary>UTC instant the customer-service window expires.</summary>
    public required DateTimeOffset ExpiresAt { get; init; }

    /// <summary>Window kind: customer_service (24h) | ctwa (72h free).</summary>
    public required string WindowType { get; init; }
}
