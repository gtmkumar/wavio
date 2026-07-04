namespace wavio.SharedDataModel.Entities.Messaging;

/// <summary>
/// Graph-API dispatch queue entry (messaging.outbound_outbox, issue #14) — written in the SAME
/// transaction as the <see cref="wavio.SharedDataModel.Entities.Messaging.OutboundMessage"/> it
/// dispatches (transactional outbox pattern). Deliberately NOT RLS-scoped: the dispatcher drains
/// every tenant's queue in one scan with no tenant context (see
/// db/migrations/V007__messaging.sql's table comment) — distinct from
/// <c>kernel.outbox_events</c> (the domain-event outbox), which has the same rationale.
/// </summary>
public class OutboundOutboxEntry
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid OutboundMessageId { get; set; }
    public Guid PhoneNumberId { get; set; }

    /// <summary>pending -> dispatching (leased via LockedBy/LockedAt) -> dispatched | failed
    /// (retryable, NextAttemptAt backoff) | dead (permanent Graph error or attempts exhausted).</summary>
    public string Status { get; set; } = "pending";

    public short Attempts { get; set; }
    public short MaxAttempts { get; set; } = 5;
    public DateTimeOffset NextAttemptAt { get; set; }

    /// <summary>Dispatcher instance id holding the lease, null when not currently leased.</summary>
    public string? LockedBy { get; set; }
    public DateTimeOffset? LockedAt { get; set; }

    public string? LastErrorCode { get; set; }
    public string? LastError { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public Guid? CreatedBy { get; set; }
    public Guid? UpdatedBy { get; set; }
}
