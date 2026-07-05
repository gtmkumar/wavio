using wavio.SharedDataModel.Entities.Quality;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace wavio.SharedDataModel.Persistence.Configurations.Quality;

public sealed class NumberQualityEventConfiguration : IEntityTypeConfiguration<NumberQualityEvent>
{
    public void Configure(EntityTypeBuilder<NumberQualityEvent> builder)
    {
        builder.ToTable("number_quality_events", "quality");

        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).HasColumnName("id").ValueGeneratedOnAdd();

        builder.Property(e => e.TenantId).HasColumnName("tenant_id").IsRequired();
        builder.Property(e => e.PhoneNumberId).HasColumnName("phone_number_id").IsRequired();
        builder.Property(e => e.OldRating).HasColumnName("old_rating").HasMaxLength(10);
        builder.Property(e => e.NewRating).HasColumnName("new_rating").HasMaxLength(10).IsRequired();
        builder.Property(e => e.EventSource).HasColumnName("event_source").HasMaxLength(20).IsRequired();
        builder.Property(e => e.Payload).HasColumnName("payload").HasColumnType("jsonb");
        builder.Property(e => e.OccurredAt).HasColumnName("occurred_at").IsRequired();
        builder.Property(e => e.CreatedAt).HasColumnName("created_at").IsRequired();
        builder.Property(e => e.CreatedBy).HasColumnName("created_by");

        builder.HasIndex(e => new { e.PhoneNumberId, e.OccurredAt })
            .HasDatabaseName("number_quality_events_number_occurred_at_idx");
        builder.HasIndex(e => e.TenantId)
            .HasDatabaseName("number_quality_events_tenant_id_idx");
    }
}
