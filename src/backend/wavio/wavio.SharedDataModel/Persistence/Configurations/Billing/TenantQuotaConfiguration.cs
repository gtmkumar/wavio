using wavio.SharedDataModel.Entities.Billing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace wavio.SharedDataModel.Persistence.Configurations.Billing;

public sealed class TenantQuotaConfiguration : IEntityTypeConfiguration<TenantQuota>
{
    public void Configure(EntityTypeBuilder<TenantQuota> builder)
    {
        builder.ToTable("tenant_quotas", "billing");

        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).HasColumnName("id").ValueGeneratedOnAdd();

        builder.Property(e => e.TenantId).HasColumnName("tenant_id").IsRequired();
        builder.Property(e => e.Category).HasColumnName("category").HasMaxLength(30).IsRequired();
        builder.Property(e => e.Period).HasColumnName("period").HasMaxLength(10).IsRequired();
        builder.Property(e => e.LimitUnit).HasColumnName("limit_unit").HasMaxLength(10).IsRequired();
        builder.Property(e => e.SoftLimit).HasColumnName("soft_limit");
        builder.Property(e => e.HardLimit).HasColumnName("hard_limit");
        builder.Property(e => e.Currency).HasColumnName("currency").HasMaxLength(3);
        builder.Property(e => e.Enabled).HasColumnName("enabled").IsRequired();

        builder.Property(e => e.CreatedAt).HasColumnName("created_at").IsRequired();
        builder.Property(e => e.UpdatedAt).HasColumnName("updated_at").IsRequired();
        builder.Property(e => e.CreatedBy).HasColumnName("created_by");
        builder.Property(e => e.UpdatedBy).HasColumnName("updated_by");
        builder.Property(e => e.Version).HasColumnName("version").IsRequired();

        builder.HasIndex(e => new { e.TenantId, e.Category, e.Period })
            .IsUnique()
            .HasDatabaseName("tenant_quotas_tenant_id_category_period_key");
    }
}
