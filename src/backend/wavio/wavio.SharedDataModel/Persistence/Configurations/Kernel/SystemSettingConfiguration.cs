using wavio.SharedDataModel.Entities.Kernel;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace wavio.SharedDataModel.Persistence.Configurations.Kernel;

public sealed class SystemSettingConfiguration : IEntityTypeConfiguration<SystemSetting>
{
    public void Configure(EntityTypeBuilder<SystemSetting> b)
    {
        b.ToTable("system_settings", "kernel");

        b.HasKey(e => e.Id);
        b.Property(e => e.Id).HasColumnName("id").ValueGeneratedOnAdd();

        b.Property(e => e.TenantId).HasColumnName("tenant_id");
        b.Property(e => e.ScopeType).HasColumnName("scope_type").HasMaxLength(20).IsRequired();
        b.Property(e => e.Category).HasColumnName("category").HasMaxLength(50).IsRequired();
        b.Property(e => e.SettingKey).HasColumnName("setting_key").HasMaxLength(100).IsRequired();
        b.Property(e => e.SettingValue).HasColumnName("setting_value").HasColumnType("jsonb").IsRequired();
        b.Property(e => e.DataType).HasColumnName("data_type").HasMaxLength(20).IsRequired();
        b.Property(e => e.Description).HasColumnName("description");
        b.Property(e => e.IsEncrypted).HasColumnName("is_encrypted").IsRequired();
        b.Property(e => e.IsReadonly).HasColumnName("is_readonly").IsRequired();
        b.Property(e => e.RequiresRestart).HasColumnName("requires_restart").IsRequired();
        b.Property(e => e.ValidationSchema).HasColumnName("validation_schema").HasColumnType("jsonb");
        b.Property(e => e.DefaultValue).HasColumnName("default_value").HasColumnType("jsonb");
        b.Property(e => e.CreatedAt).HasColumnName("created_at").IsRequired();
        b.Property(e => e.UpdatedAt).HasColumnName("updated_at").IsRequired();
        b.Property(e => e.UpdatedBy).HasColumnName("updated_by");
        b.Property(e => e.Version).HasColumnName("version").IsRequired();
        b.Property(e => e.CreatedBy).HasColumnName("created_by");
        b.Property(e => e.Status).HasColumnName("status").HasMaxLength(20).IsRequired();

        b.HasIndex(e => new { e.ScopeType, e.TenantId, e.Category, e.SettingKey })
            .IsUnique()
            .HasDatabaseName("system_settings_scope_type_tenant_id_category_setting_key_key");
    }
}
