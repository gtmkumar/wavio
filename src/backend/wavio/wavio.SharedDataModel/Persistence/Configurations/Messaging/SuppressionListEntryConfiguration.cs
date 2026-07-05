using wavio.SharedDataModel.Entities.Messaging;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace wavio.SharedDataModel.Persistence.Configurations.Messaging;

public sealed class SuppressionListEntryConfiguration : IEntityTypeConfiguration<SuppressionListEntry>
{
    public void Configure(EntityTypeBuilder<SuppressionListEntry> builder)
    {
        builder.ToTable("suppression_list", "messaging");

        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).HasColumnName("id").ValueGeneratedOnAdd();

        builder.Property(e => e.TenantId).HasColumnName("tenant_id").IsRequired();
        builder.Property(e => e.WaId).HasColumnName("wa_id").HasMaxLength(20).IsRequired();
        builder.Property(e => e.Reason).HasColumnName("reason").HasMaxLength(30).IsRequired();
        builder.Property(e => e.Source).HasColumnName("source").HasMaxLength(50);
        builder.Property(e => e.Notes).HasColumnName("notes");
        builder.Property(e => e.ExpiresAt).HasColumnName("expires_at");
        builder.Property(e => e.CreatedAt).HasColumnName("created_at").IsRequired();
        builder.Property(e => e.UpdatedAt).HasColumnName("updated_at").IsRequired();
        builder.Property(e => e.CreatedBy).HasColumnName("created_by");
        builder.Property(e => e.UpdatedBy).HasColumnName("updated_by");

        builder.HasIndex(e => new { e.TenantId, e.WaId })
            .HasDatabaseName("suppression_list_tenant_id_wa_id_key")
            .IsUnique();
    }
}
