using wavio.SharedDataModel.Entities.Templates;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace wavio.SharedDataModel.Persistence.Configurations.Templates;

public sealed class TemplateVersionConfiguration : IEntityTypeConfiguration<TemplateVersion>
{
    public void Configure(EntityTypeBuilder<TemplateVersion> builder)
    {
        builder.ToTable("template_versions", "templates");

        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).HasColumnName("id").ValueGeneratedOnAdd();

        builder.Property(e => e.TenantId).HasColumnName("tenant_id").IsRequired();
        builder.Property(e => e.TemplateId).HasColumnName("template_id").IsRequired();
        builder.Property(e => e.VersionNumber).HasColumnName("version_number").IsRequired();
        builder.Property(e => e.Components).HasColumnName("components").HasColumnType("jsonb").IsRequired();
        builder.Property(e => e.ExampleValues).HasColumnName("example_values").HasColumnType("jsonb");
        builder.Property(e => e.Status).HasColumnName("status").HasMaxLength(20).IsRequired();
        builder.Property(e => e.RejectionReason).HasColumnName("rejection_reason");
        builder.Property(e => e.SubmittedAt).HasColumnName("submitted_at");
        builder.Property(e => e.ReviewedAt).HasColumnName("reviewed_at");
        builder.Property(e => e.CreatedAt).HasColumnName("created_at").IsRequired();
        builder.Property(e => e.UpdatedAt).HasColumnName("updated_at").IsRequired();
        builder.Property(e => e.CreatedBy).HasColumnName("created_by");
        builder.Property(e => e.UpdatedBy).HasColumnName("updated_by");

        builder.HasIndex(e => new { e.TemplateId, e.VersionNumber })
            .IsUnique()
            .HasDatabaseName("template_versions_template_id_version_number_key");
        builder.HasIndex(e => e.TenantId).HasDatabaseName("template_versions_tenant_id_idx");

        // No navigation property (keeps the entity a plain POCO, matching this codebase's
        // convention of not modeling every cross-entity relationship) — but EF still needs to
        // KNOW about the FK so its SaveChanges dependency graph orders a new TemplateVersion
        // after its parent Template within the same batch. Without this, insertion order across
        // several newly-Added entities is an implementation detail EF does not guarantee by add
        // order alone (confirmed live: AuditSaveChangesInterceptor's extra audit_logs inserts
        // reordered the batch enough to violate template_versions_template_id_fkey).
        builder.HasOne<Template>().WithMany()
            .HasForeignKey(e => e.TemplateId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
