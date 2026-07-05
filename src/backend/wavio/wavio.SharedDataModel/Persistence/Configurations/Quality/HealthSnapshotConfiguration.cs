using wavio.SharedDataModel.Entities.Quality;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace wavio.SharedDataModel.Persistence.Configurations.Quality;

public sealed class HealthSnapshotConfiguration : IEntityTypeConfiguration<HealthSnapshot>
{
    public void Configure(EntityTypeBuilder<HealthSnapshot> builder)
    {
        builder.ToTable("health_snapshots", "quality");

        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).HasColumnName("id").ValueGeneratedOnAdd();

        builder.Property(e => e.TenantId).HasColumnName("tenant_id").IsRequired();
        builder.Property(e => e.PhoneNumberId).HasColumnName("phone_number_id").IsRequired();
        builder.Property(e => e.PeriodStart).HasColumnName("period_start").HasColumnType("date").IsRequired();
        builder.Property(e => e.PeriodEnd).HasColumnName("period_end").HasColumnType("date").IsRequired();
        builder.Property(e => e.DeliveryRate).HasColumnName("delivery_rate").HasColumnType("numeric(5,2)");
        builder.Property(e => e.ReadRate).HasColumnName("read_rate").HasColumnType("numeric(5,2)");
        builder.Property(e => e.BlockProxyRate).HasColumnName("block_proxy_rate").HasColumnType("numeric(5,2)");
        builder.Property(e => e.QualityRating).HasColumnName("quality_rating").HasMaxLength(10);
        builder.Property(e => e.MessagingTier).HasColumnName("messaging_tier").HasMaxLength(20);
        builder.Property(e => e.TierHeadroom).HasColumnName("tier_headroom");
        builder.Property(e => e.MessagesSent).HasColumnName("messages_sent").IsRequired();
        builder.Property(e => e.MessagesDelivered).HasColumnName("messages_delivered").IsRequired();
        builder.Property(e => e.MessagesRead).HasColumnName("messages_read").IsRequired();
        builder.Property(e => e.MessagesFailed).HasColumnName("messages_failed").IsRequired();
        builder.Property(e => e.Metrics).HasColumnName("metrics").HasColumnType("jsonb");
        builder.Property(e => e.CreatedAt).HasColumnName("created_at").IsRequired();
        builder.Property(e => e.CreatedBy).HasColumnName("created_by");

        builder.HasIndex(e => new { e.PhoneNumberId, e.PeriodStart })
            .IsUnique()
            .HasDatabaseName("health_snapshots_number_period_start_key");
        builder.HasIndex(e => e.TenantId)
            .HasDatabaseName("health_snapshots_tenant_id_idx");
    }
}
