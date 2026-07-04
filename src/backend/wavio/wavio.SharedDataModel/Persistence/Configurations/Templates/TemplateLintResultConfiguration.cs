using wavio.SharedDataModel.Entities.Templates;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace wavio.SharedDataModel.Persistence.Configurations.Templates;

public sealed class TemplateLintResultConfiguration : IEntityTypeConfiguration<TemplateLintResult>
{
    public void Configure(EntityTypeBuilder<TemplateLintResult> builder)
    {
        builder.ToTable("template_lint_results", "templates");

        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).HasColumnName("id").ValueGeneratedOnAdd();

        builder.Property(e => e.TenantId).HasColumnName("tenant_id").IsRequired();
        builder.Property(e => e.TemplateVersionId).HasColumnName("template_version_id").IsRequired();
        builder.Property(e => e.Linter).HasColumnName("linter").HasMaxLength(30).IsRequired();
        builder.Property(e => e.Passed).HasColumnName("passed").IsRequired();
        builder.Property(e => e.Findings).HasColumnName("findings").HasColumnType("jsonb").IsRequired();
        builder.Property(e => e.Score).HasColumnName("score");
        builder.Property(e => e.CreatedAt).HasColumnName("created_at").IsRequired();
        builder.Property(e => e.CreatedBy).HasColumnName("created_by");

        builder.HasIndex(e => e.TemplateVersionId).HasDatabaseName("template_lint_results_template_version_id_idx");
        builder.HasIndex(e => e.TenantId).HasDatabaseName("template_lint_results_tenant_id_idx");

        // See TemplateVersionConfiguration's comment: EF needs the FK in its model to order a new
        // TemplateLintResult after its TemplateVersion within the same SaveChanges batch.
        builder.HasOne<TemplateVersion>().WithMany()
            .HasForeignKey(e => e.TemplateVersionId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
