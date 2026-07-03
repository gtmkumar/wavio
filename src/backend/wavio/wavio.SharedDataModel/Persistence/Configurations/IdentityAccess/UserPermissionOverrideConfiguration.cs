using wavio.SharedDataModel.Entities.IdentityAccess;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace wavio.SharedDataModel.Persistence.Configurations.IdentityAccess;

public sealed class UserPermissionOverrideConfiguration : IEntityTypeConfiguration<UserPermissionOverride>
{
    public void Configure(EntityTypeBuilder<UserPermissionOverride> b)
    {
        b.ToTable("user_permission_override", "identity_access");

        // Surrogate PK: a user may hold one global + several scoped overrides for the same
        // permission, so the natural key (user, permission, scope) can't be the PK. Uniqueness
        // on that natural key is enforced by a filtered/expression unique index in the SQL patch
        // (COALESCE over the nullable scope columns) — mirrored here as a best-effort index.
        b.HasKey(e => e.Id);
        b.Property(e => e.Id).HasColumnName("id").ValueGeneratedNever();
        b.Property(e => e.UserId).HasColumnName("user_id").IsRequired();
        b.Property(e => e.PermissionId).HasColumnName("permission_id").IsRequired();
        b.Property(e => e.Effect).HasColumnName("effect").HasMaxLength(16).IsRequired();
        b.Property(e => e.ScopeType).HasColumnName("scope_type").HasMaxLength(20);
        b.Property(e => e.ScopeId).HasColumnName("scope_id");
        b.Property(e => e.Reason).HasColumnName("reason");
        b.Property(e => e.ExpiresAt).HasColumnName("expires_at");
        b.Property(e => e.GrantedAt).HasColumnName("granted_at").IsRequired();
        b.Property(e => e.GrantedBy).HasColumnName("granted_by");
        b.Property(e => e.CreatedAt).HasColumnName("created_at").IsRequired();

        b.HasOne(e => e.Permission).WithMany().HasForeignKey(e => e.PermissionId);
        b.HasIndex(e => e.UserId);
    }
}
