namespace wavio.SharedDataModel.Entities.Quality;

/// <summary>
/// Append-only log of a phone number's quality-rating transitions (quality.number_quality_events,
/// issue #20, db/migrations/V011__quality.sql) — one row per actual change (not one per webhook
/// delivery; a redelivered no-op is not re-logged, see RecordQualityChangeHandler). Same
/// append-only shape as <see cref="wavio.SharedDataModel.Entities.Sessions.WindowEvent"/> — no
/// Version/UpdatedAt.
/// </summary>
public class NumberQualityEvent
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid PhoneNumberId { get; set; }

    /// <summary>green | yellow | red | unknown (lowercase, CHECK-enforced) — null only for the
    /// very first event on a number that had no prior recorded rating.</summary>
    public string? OldRating { get; set; }

    /// <summary>green | yellow | red | unknown (lowercase, CHECK-enforced).</summary>
    public string NewRating { get; set; } = null!;

    /// <summary>webhook | manual | simulated (CHECK-enforced).</summary>
    public string EventSource { get; set; } = "webhook";

    /// <summary>Raw webhook payload (jsonb), diagnostic only.</summary>
    public string? Payload { get; set; }

    public DateTimeOffset OccurredAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public Guid? CreatedBy { get; set; }
}
