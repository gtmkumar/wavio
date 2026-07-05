using wavio.SharedDataModel.Entities.Consent;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace wavio.SharedDataModel.Persistence.Configurations.Consent;

public sealed class RetentionPolicyConfiguration : IEntityTypeConfiguration<RetentionPolicy>
{
    public void Configure(EntityTypeBuilder<RetentionPolicy> builder)
    {
        builder.ToTable("retention_policies", "consent");

        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).HasColumnName("id").ValueGeneratedOnAdd();

        builder.Property(e => e.TenantId).HasColumnName("tenant_id");
        builder.Property(e => e.DataClass).HasColumnName("data_class").HasMaxLength(30).IsRequired();
        builder.Property(e => e.RetentionDays).HasColumnName("retention_days").IsRequired();
        builder.Property(e => e.Basis).HasColumnName("basis").HasMaxLength(30);
        builder.Property(e => e.Enabled).HasColumnName("enabled").IsRequired();
        builder.Property(e => e.CreatedAt).HasColumnName("created_at").IsRequired();
        builder.Property(e => e.UpdatedAt).HasColumnName("updated_at").IsRequired();
        builder.Property(e => e.CreatedBy).HasColumnName("created_by");
        builder.Property(e => e.UpdatedBy).HasColumnName("updated_by");
        builder.Property(e => e.Version).HasColumnName("version").IsRequired();

        // NULLS NOT DISTINCT (V012) — EF's HasIndex().IsUnique() maps to a plain unique index by
        // default (NULLS DISTINCT semantics); the real constraint already exists in the DB via
        // the migration. Declared here descriptively (see ErasureRequestConfiguration's status
        // index comment for why) rather than to drive schema generation.
        builder.HasIndex(e => new { e.TenantId, e.DataClass })
            .HasDatabaseName("retention_policies_tenant_id_data_class_key")
            .IsUnique();
    }
}
