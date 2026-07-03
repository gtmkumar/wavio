using wavio.SharedDataModel.Entities.Kernel;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace wavio.SharedDataModel.Persistence.Configurations.Kernel;

public sealed class OutboxEventConfiguration : IEntityTypeConfiguration<OutboxEvent>
{
    public void Configure(EntityTypeBuilder<OutboxEvent> b)
    {
        b.ToTable("outbox_events", "kernel");

        b.HasKey(e => e.Id);
        b.Property(e => e.Id).HasColumnName("id").ValueGeneratedOnAdd();

        b.Property(e => e.TenantId).HasColumnName("tenant_id");
        b.Property(e => e.AggregateType).HasColumnName("aggregate_type").HasMaxLength(100).IsRequired();
        b.Property(e => e.AggregateId).HasColumnName("aggregate_id").IsRequired();
        b.Property(e => e.EventType).HasColumnName("event_type").HasMaxLength(100).IsRequired();
        b.Property(e => e.EventVersion).HasColumnName("event_version").IsRequired();
        b.Property(e => e.Payload).HasColumnName("payload").HasColumnType("jsonb").IsRequired();
        b.Property(e => e.Metadata).HasColumnName("metadata").HasColumnType("jsonb").IsRequired();
        b.Property(e => e.CorrelationId).HasColumnName("correlation_id");
        b.Property(e => e.CausationId).HasColumnName("causation_id");
        b.Property(e => e.OccurredAt).HasColumnName("occurred_at").IsRequired();
        b.Property(e => e.PublishedAt).HasColumnName("published_at");
        b.Property(e => e.PublishAttempts).HasColumnName("publish_attempts").IsRequired();
        b.Property(e => e.NextAttemptAt).HasColumnName("next_attempt_at");
        b.Property(e => e.LastError).HasColumnName("last_error");
        b.Property(e => e.Status).HasColumnName("status").HasMaxLength(20).IsRequired();
        b.Property(e => e.RoutingKey).HasColumnName("routing_key").HasMaxLength(200);
        b.Property(e => e.TargetExchange).HasColumnName("target_exchange").HasMaxLength(100);
        b.Property(e => e.IdempotencyKey).HasColumnName("idempotency_key").HasMaxLength(100);
        b.Property(e => e.CreatedAt).HasColumnName("created_at").IsRequired();
        b.Property(e => e.CreatedBy).HasColumnName("created_by");

        b.HasIndex(e => e.IdempotencyKey).IsUnique().HasDatabaseName("outbox_events_idempotency_key_key");
    }
}
