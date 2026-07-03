namespace wavio.SharedDataModel.Entities.Kernel;

/// <summary>Transactional outbox event for reliable messaging (kernel.outbox_events).
/// Has created_at, created_by — no updated_at, no version, no deleted_at.</summary>
public class OutboxEvent
{
    public Guid Id { get; set; }
    public Guid? TenantId { get; set; }
    public string AggregateType { get; set; } = null!;
    public Guid AggregateId { get; set; }
    public string EventType { get; set; } = null!;
    public short EventVersion { get; set; }
    public string Payload { get; set; } = null!;
    public string Metadata { get; set; } = null!;
    public Guid? CorrelationId { get; set; }
    public Guid? CausationId { get; set; }
    public DateTimeOffset OccurredAt { get; set; }
    public DateTimeOffset? PublishedAt { get; set; }
    public short PublishAttempts { get; set; }
    public DateTimeOffset? NextAttemptAt { get; set; }
    public string? LastError { get; set; }
    public string Status { get; set; } = null!;
    public string? RoutingKey { get; set; }
    public string? TargetExchange { get; set; }
    public string? IdempotencyKey { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public Guid? CreatedBy { get; set; }
}
