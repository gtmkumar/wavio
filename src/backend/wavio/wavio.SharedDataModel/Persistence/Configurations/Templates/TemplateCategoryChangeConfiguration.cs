using wavio.SharedDataModel.Entities.Templates;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace wavio.SharedDataModel.Persistence.Configurations.Templates;

public sealed class TemplateCategoryChangeConfiguration : IEntityTypeConfiguration<TemplateCategoryChange>
{
    public void Configure(EntityTypeBuilder<TemplateCategoryChange> builder)
    {
        builder.ToTable("template_category_changes", "templates");

        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).HasColumnName("id").ValueGeneratedOnAdd();

        builder.Property(e => e.TenantId).HasColumnName("tenant_id").IsRequired();
        builder.Property(e => e.TemplateId).HasColumnName("template_id").IsRequired();
        builder.Property(e => e.OldCategory).HasColumnName("old_category").HasMaxLength(20).IsRequired();
        builder.Property(e => e.NewCategory).HasColumnName("new_category").HasMaxLength(20).IsRequired();
        builder.Property(e => e.OccurredAt).HasColumnName("occurred_at").IsRequired();
        builder.Property(e => e.TenantAlertedAt).HasColumnName("tenant_alerted_at");
        builder.Property(e => e.BillingRecalibratedAt).HasColumnName("billing_recalibrated_at");
        builder.Property(e => e.Payload).HasColumnName("payload").HasColumnType("jsonb");
        builder.Property(e => e.CreatedAt).HasColumnName("created_at").IsRequired();
        builder.Property(e => e.UpdatedAt).HasColumnName("updated_at").IsRequired();
        builder.Property(e => e.CreatedBy).HasColumnName("created_by");
        builder.Property(e => e.UpdatedBy).HasColumnName("updated_by");

        builder.HasIndex(e => e.TemplateId).HasDatabaseName("template_category_changes_template_id_idx");
        builder.HasIndex(e => e.TenantId).HasDatabaseName("template_category_changes_tenant_id_idx");

        // See TemplateVersionConfiguration's comment: EF needs the FK in its model for correct
        // SaveChanges ordering.
        builder.HasOne<Template>().WithMany()
            .HasForeignKey(e => e.TemplateId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
