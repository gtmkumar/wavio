using wavio.SharedDataModel.Entities.Kernel;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace wavio.SharedDataModel.Persistence.Configurations.Kernel;

public sealed class OutboxConsumedEventConfiguration : IEntityTypeConfiguration<OutboxConsumedEvent>
{
    public void Configure(EntityTypeBuilder<OutboxConsumedEvent> b)
    {
        b.ToTable("outbox_consumed_events", "kernel");

        // Composite key: one marker per (consumer, event). Lets multiple consumers track the same
        // event id independently, and lets the consumer anti-join on its own consumer_name.
        b.HasKey(e => new { e.ConsumerName, e.EventId });

        b.Property(e => e.ConsumerName).HasColumnName("consumer_name").HasMaxLength(100).IsRequired();
        b.Property(e => e.EventId).HasColumnName("event_id").IsRequired();
        b.Property(e => e.ProcessedAt).HasColumnName("processed_at").IsRequired();
    }
}
