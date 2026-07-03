using wavio.SharedDataModel.Entities.IdentityAccess;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace wavio.SharedDataModel.Persistence.Configurations.IdentityAccess;

public sealed class RolePermissionConfiguration : IEntityTypeConfiguration<RolePermission>
{
    public void Configure(EntityTypeBuilder<RolePermission> b)
    {
        b.ToTable("role_permissions", "identity_access");

        b.HasKey(e => e.Id);
        b.Property(e => e.Id).HasColumnName("id").ValueGeneratedOnAdd();

        b.Property(e => e.RoleId).HasColumnName("role_id").IsRequired();
        b.Property(e => e.PermissionId).HasColumnName("permission_id").IsRequired();
        b.Property(e => e.Effect).HasColumnName("effect").HasMaxLength(16).IsRequired();
        b.Property(e => e.GrantedAt).HasColumnName("granted_at").IsRequired();
        b.Property(e => e.GrantedBy).HasColumnName("granted_by");
        b.Property(e => e.CreatedAt).HasColumnName("created_at").IsRequired();
        b.Property(e => e.CreatedBy).HasColumnName("created_by");

        b.HasIndex(e => new { e.RoleId, e.PermissionId }).IsUnique().HasDatabaseName("role_permissions_role_id_permission_id_key");

        b.HasOne(e => e.Role)
            .WithMany(r => r.RolePermissions)
            .HasForeignKey(e => e.RoleId)
            .OnDelete(DeleteBehavior.Cascade)
            .HasConstraintName("role_permissions_role_id_fkey");

        b.HasOne(e => e.Permission)
            .WithMany(p => p.RolePermissions)
            .HasForeignKey(e => e.PermissionId)
            .OnDelete(DeleteBehavior.Cascade)
            .HasConstraintName("role_permissions_permission_id_fkey");
    }
}
