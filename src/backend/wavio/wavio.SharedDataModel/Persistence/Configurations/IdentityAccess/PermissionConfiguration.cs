using wavio.SharedDataModel.Entities.IdentityAccess;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace wavio.SharedDataModel.Persistence.Configurations.IdentityAccess;

public sealed class PermissionConfiguration : IEntityTypeConfiguration<Permission>
{
    public void Configure(EntityTypeBuilder<Permission> b)
    {
        b.ToTable("permissions", "identity_access");

        b.HasKey(e => e.Id);
        b.Property(e => e.Id).HasColumnName("id").ValueGeneratedOnAdd();

        b.Property(e => e.Code).HasColumnName("code").HasMaxLength(100).IsRequired();
        b.Property(e => e.Module).HasColumnName("module").HasMaxLength(50).IsRequired();
        b.Property(e => e.ModuleKey).HasColumnName("module_key").HasMaxLength(64);
        b.Property(e => e.Action).HasColumnName("action").HasMaxLength(50).IsRequired();
        b.Property(e => e.Name).HasColumnName("name").HasMaxLength(200).IsRequired();
        b.Property(e => e.Description).HasColumnName("description");
        b.Property(e => e.IsSystem).HasColumnName("is_system").IsRequired();
        b.Property(e => e.RequiresScope).HasColumnName("requires_scope").IsRequired();
        b.Property(e => e.RiskLevel).HasColumnName("risk_level").HasMaxLength(20).IsRequired();
        b.Property(e => e.CreatedAt).HasColumnName("created_at").IsRequired();
        b.Property(e => e.UpdatedAt).HasColumnName("updated_at").IsRequired();
        b.Property(e => e.CreatedBy).HasColumnName("created_by");
        b.Property(e => e.UpdatedBy).HasColumnName("updated_by");
        b.Property(e => e.Status).HasColumnName("status").HasMaxLength(20).IsRequired();

        b.HasIndex(e => e.Code).IsUnique().HasDatabaseName("permissions_code_key");
    }
}
