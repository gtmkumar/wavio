using wavio.SharedDataModel.Entities.Templates;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace wavio.SharedDataModel.Persistence.Configurations.Templates;

public sealed class TemplateConfiguration : IEntityTypeConfiguration<Template>
{
    public void Configure(EntityTypeBuilder<Template> builder)
    {
        builder.ToTable("templates", "templates");

        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).HasColumnName("id").ValueGeneratedOnAdd();

        builder.Property(e => e.TenantId).HasColumnName("tenant_id").IsRequired();
        builder.Property(e => e.BusinessAccountId).HasColumnName("business_account_id").IsRequired();
        builder.Property(e => e.Name).HasColumnName("name").HasMaxLength(512).IsRequired();
        builder.Property(e => e.Language).HasColumnName("language").HasMaxLength(15).IsRequired();
        builder.Property(e => e.Category).HasColumnName("category").HasMaxLength(20).IsRequired();
        builder.Property(e => e.MetaTemplateId).HasColumnName("meta_template_id").HasMaxLength(64);
        builder.Property(e => e.Status).HasColumnName("status").HasMaxLength(20).IsRequired();
        builder.Property(e => e.CurrentVersionId).HasColumnName("current_version_id");
        builder.Property(e => e.PausedUntil).HasColumnName("paused_until");
        builder.Property(e => e.PauseCount).HasColumnName("pause_count").IsRequired();
        builder.Property(e => e.CreatedAt).HasColumnName("created_at").IsRequired();
        builder.Property(e => e.UpdatedAt).HasColumnName("updated_at").IsRequired();
        builder.Property(e => e.CreatedBy).HasColumnName("created_by");
        builder.Property(e => e.UpdatedBy).HasColumnName("updated_by");
        builder.Property(e => e.Version).HasColumnName("version").IsRequired();
        builder.Property(e => e.DeletedAt).HasColumnName("deleted_at");

        builder.HasIndex(e => new { e.BusinessAccountId, e.Name, e.Language })
            .IsUnique()
            .HasDatabaseName("templates_business_account_id_name_language_key");
        builder.HasIndex(e => e.TenantId).HasDatabaseName("templates_tenant_id_idx");
        builder.HasIndex(e => e.BusinessAccountId).HasDatabaseName("templates_business_account_id_idx");

        builder.HasQueryFilter(e => e.DeletedAt == null);

        // Deliberately NOT modeled as an EF relationship, unlike the other FKs in this schema
        // (see TemplateVersionConfiguration's comment): CurrentVersionId and
        // TemplateVersion.TemplateId form a genuine circular pair (matches the migration's own
        // comment: "Circular pair with templates.current_version_id — added after both exist").
        // Configuring BOTH directions as EF relationships makes EF throw "circular dependency
        // detected" when a Template and its first TemplateVersion are both newly Added in the
        // same SaveChanges — EF's automatic cycle-breaking (null the FK, insert, then UPDATE)
        // does not kick in here (confirmed live). Callers instead set CurrentVersionId only after
        // the Template row already exists, saving in two steps around that one assignment — see
        // CreateTemplateCommandHandler.
    }
}
