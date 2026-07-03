using wavio.SharedDataModel.Entities.IdentityAccess;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace wavio.SharedDataModel.Persistence.Configurations.IdentityAccess;

public sealed class UserScopeMembershipConfiguration : IEntityTypeConfiguration<UserScopeMembership>
{
    public void Configure(EntityTypeBuilder<UserScopeMembership> b)
    {
        b.ToTable("user_scope_memberships", "identity_access");

        b.HasKey(e => e.Id);
        b.Property(e => e.Id).HasColumnName("id").ValueGeneratedOnAdd();

        b.Property(e => e.UserId).HasColumnName("user_id").IsRequired();
        b.Property(e => e.ScopeType).HasColumnName("scope_type").HasMaxLength(20).IsRequired();
        b.Property(e => e.ScopeId).HasColumnName("scope_id");
        b.Property(e => e.RoleId).HasColumnName("role_id").IsRequired();
        b.Property(e => e.IsPrimary).HasColumnName("is_primary").IsRequired();
        b.Property(e => e.GrantedAt).HasColumnName("granted_at").IsRequired();
        b.Property(e => e.GrantedBy).HasColumnName("granted_by");
        b.Property(e => e.RevokedAt).HasColumnName("revoked_at");
        b.Property(e => e.RevokedBy).HasColumnName("revoked_by");
        b.Property(e => e.RevokedReason).HasColumnName("revoked_reason");
        b.Property(e => e.ExpiresAt).HasColumnName("expires_at");
        b.Property(e => e.Metadata).HasColumnName("metadata").HasColumnType("jsonb").IsRequired();
        b.Property(e => e.CreatedAt).HasColumnName("created_at").IsRequired();
        b.Property(e => e.CreatedBy).HasColumnName("created_by");

        b.HasIndex(e => new { e.UserId, e.ScopeType, e.ScopeId, e.RoleId })
            .IsUnique()
            .HasDatabaseName("user_scope_memberships_user_id_scope_type_scope_id_role_id_key");

        b.HasOne(e => e.User)
            .WithMany(u => u.ScopeMemberships)
            .HasForeignKey(e => e.UserId)
            .OnDelete(DeleteBehavior.Cascade)
            .HasConstraintName("user_scope_memberships_user_id_fkey");

        b.HasOne(e => e.Role)
            .WithMany(r => r.UserScopeMemberships)
            .HasForeignKey(e => e.RoleId)
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("user_scope_memberships_role_id_fkey");
    }
}
