using wavio.SharedDataModel.Entities.Ingest;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace wavio.SharedDataModel.Persistence.Configurations.Ingest;

public sealed class WebhookDedupeConfiguration : IEntityTypeConfiguration<WebhookDedupe>
{
    public void Configure(EntityTypeBuilder<WebhookDedupe> builder)
    {
        builder.ToTable("webhook_dedupe", "ingest");

        builder.HasKey(e => new { e.Wamid, e.EventType });
        builder.Property(e => e.Wamid).HasColumnName("wamid").HasMaxLength(128);
        builder.Property(e => e.EventType).HasColumnName("event_type").HasMaxLength(50);

        builder.Property(e => e.FirstSeenAt).HasColumnName("first_seen_at").IsRequired();
        builder.Property(e => e.CreatedAt).HasColumnName("created_at").IsRequired();
        builder.Property(e => e.CreatedBy).HasColumnName("created_by");
    }
}
