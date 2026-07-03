using wavio.SharedDataModel.Entities.Templates;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace wavio.SharedDataModel.Persistence.Configurations.Templates;

public sealed class TemplateStatusEventConfiguration : IEntityTypeConfiguration<TemplateStatusEvent>
{
    public void Configure(EntityTypeBuilder<TemplateStatusEvent> builder)
    {
        builder.ToTable("template_status_events", "templates");

        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).HasColumnName("id").ValueGeneratedOnAdd();

        builder.Property(e => e.TenantId).HasColumnName("tenant_id").IsRequired();
        builder.Property(e => e.TemplateId).HasColumnName("template_id").IsRequired();
        builder.Property(e => e.TemplateVersionId).HasColumnName("template_version_id");
        builder.Property(e => e.OldStatus).HasColumnName("old_status").HasMaxLength(20);
        builder.Property(e => e.NewStatus).HasColumnName("new_status").HasMaxLength(20).IsRequired();
        builder.Property(e => e.Reason).HasColumnName("reason");
        builder.Property(e => e.Payload).HasColumnName("payload").HasColumnType("jsonb");
        builder.Property(e => e.OccurredAt).HasColumnName("occurred_at").IsRequired();
        builder.Property(e => e.CreatedAt).HasColumnName("created_at").IsRequired();
        builder.Property(e => e.CreatedBy).HasColumnName("created_by");

        builder.HasIndex(e => new { e.TemplateId, e.OccurredAt })
            .HasDatabaseName("template_status_events_template_id_occurred_at_idx");
        builder.HasIndex(e => e.TenantId).HasDatabaseName("template_status_events_tenant_id_idx");

        // See TemplateVersionConfiguration's comment: EF needs both FKs in its model to order a
        // new TemplateStatusEvent after the Template/TemplateVersion it references within the
        // same SaveChanges batch (e.g. UpdateTemplateCommandHandler's "create new version"
        // branch inserts a fresh TemplateVersion and its status event together).
        builder.HasOne<Template>().WithMany()
            .HasForeignKey(e => e.TemplateId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<TemplateVersion>().WithMany()
            .HasForeignKey(e => e.TemplateVersionId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}
