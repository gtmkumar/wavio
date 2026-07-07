namespace wavio.SharedDataModel.Entities.Waba;

/// <summary>
/// waba.phone_number_events (db/migrations/V002__waba.sql) — append-only history of phone-number
/// status transitions (rows are never updated; created_* pair only). V002's column comment makes
/// the status state machine app-enforced "and logged in phone_number_events" — the onboarding
/// wizard's register step (PENDING → CONNECTED) is the first writer.
/// </summary>
public class WabaPhoneNumberEvent
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid PhoneNumberId { get; set; }
    public string EventType { get; set; } = null!;
    public string? OldStatus { get; set; }
    public string? NewStatus { get; set; }

    /// <summary>Optional jsonb detail (e.g. which Graph call drove the transition).</summary>
    public string? Payload { get; set; }

    public DateTimeOffset OccurredAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public Guid? CreatedBy { get; set; }
}
