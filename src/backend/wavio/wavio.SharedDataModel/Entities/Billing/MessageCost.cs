namespace wavio.SharedDataModel.Entities.Billing;

/// <summary>
/// One row per billed delivery — the PMP cost ledger (billing.message_costs, issue #19, ADR-002,
/// spec §4.7, §6). Append-only, sourced straight from the Meta status-webhook <c>pricing</c>
/// object: <see cref="WebhookPricing"/> carries it verbatim and is the billing source of truth;
/// <see cref="RateCardId"/> is populated ONLY as an advisory cross-reference (which card was
/// active when this was billed) — the ledger amount itself never comes from our rate card.
/// <see cref="Wamid"/> is globally UNIQUE — the idempotency key the consumer relies on (a
/// redelivered status webhook for the same message must not double-bill).
/// </summary>
public class MessageCost
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid PhoneNumberId { get; set; }
    public Guid? RateCardId { get; set; }
    public string Wamid { get; set; } = null!;

    /// <summary>marketing | utility | authentication | authentication_international | service.</summary>
    public string Category { get; set; } = null!;

    public string? PricingModel { get; set; }
    public string? PricingCategory { get; set; }
    public bool Billable { get; set; } = true;
    public decimal Amount { get; set; }
    public string Currency { get; set; } = "INR";
    public string? DestinationMarket { get; set; }

    /// <summary>Raw Meta status-webhook <c>pricing</c> object (jsonb), verbatim.</summary>
    public string? WebhookPricing { get; set; }

    public DateTimeOffset BilledAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public Guid? CreatedBy { get; set; }
}
