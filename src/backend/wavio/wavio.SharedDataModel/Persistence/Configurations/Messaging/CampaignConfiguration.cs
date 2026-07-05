using wavio.SharedDataModel.Entities.Messaging;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace wavio.SharedDataModel.Persistence.Configurations.Messaging;

public sealed class CampaignConfiguration : IEntityTypeConfiguration<Campaign>
{
    public void Configure(EntityTypeBuilder<Campaign> builder)
    {
        builder.ToTable("campaigns", "messaging");

        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).HasColumnName("id").ValueGeneratedOnAdd();

        builder.Property(e => e.TenantId).HasColumnName("tenant_id").IsRequired();
        builder.Property(e => e.PhoneNumberId).HasColumnName("phone_number_id").IsRequired();
        builder.Property(e => e.Name).HasColumnName("name").HasMaxLength(200).IsRequired();
        builder.Property(e => e.TemplateVersionId).HasColumnName("template_version_id").IsRequired();
        builder.Property(e => e.Params).HasColumnName("params").HasColumnType("jsonb");
        builder.Property(e => e.Status).HasColumnName("status").HasMaxLength(20).IsRequired();
        builder.Property(e => e.ScheduledAt).HasColumnName("scheduled_at");
        builder.Property(e => e.StartedAt).HasColumnName("started_at");
        builder.Property(e => e.CompletedAt).HasColumnName("completed_at");
        builder.Property(e => e.AudienceCount).HasColumnName("audience_count").IsRequired();
        builder.Property(e => e.SuppressedCount).HasColumnName("suppressed_count").IsRequired();
        builder.Property(e => e.SentCount).HasColumnName("sent_count").IsRequired();
        builder.Property(e => e.DeliveredCount).HasColumnName("delivered_count").IsRequired();
        builder.Property(e => e.ReadCount).HasColumnName("read_count").IsRequired();
        builder.Property(e => e.FailedCount).HasColumnName("failed_count").IsRequired();
        builder.Property(e => e.ProjectedCost).HasColumnName("projected_cost").HasColumnType("numeric(12,4)");
        builder.Property(e => e.ProjectedCurrency).HasColumnName("projected_currency").HasMaxLength(3);

        builder.Property(e => e.CreatedAt).HasColumnName("created_at").IsRequired();
        builder.Property(e => e.UpdatedAt).HasColumnName("updated_at").IsRequired();
        builder.Property(e => e.CreatedBy).HasColumnName("created_by");
        builder.Property(e => e.UpdatedBy).HasColumnName("updated_by");
        builder.Property(e => e.Version).HasColumnName("version").IsRequired();

        builder.HasIndex(e => new { e.TenantId, e.Status })
            .HasDatabaseName("campaigns_tenant_id_status_idx");
        builder.HasIndex(e => e.PhoneNumberId)
            .HasDatabaseName("campaigns_phone_number_id_idx");
        builder.HasIndex(e => e.TemplateVersionId)
            .HasDatabaseName("campaigns_template_version_id_idx");
    }
}
