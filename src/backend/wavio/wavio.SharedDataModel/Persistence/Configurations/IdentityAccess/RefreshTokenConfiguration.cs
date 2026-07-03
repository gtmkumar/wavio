using wavio.SharedDataModel.Entities.IdentityAccess;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace wavio.SharedDataModel.Persistence.Configurations.IdentityAccess;

public sealed class RefreshTokenConfiguration : IEntityTypeConfiguration<RefreshToken>
{
    public void Configure(EntityTypeBuilder<RefreshToken> b)
    {
        b.ToTable("refresh_tokens", "identity_access");

        b.HasKey(e => e.Id);
        b.Property(e => e.Id).HasColumnName("id").ValueGeneratedOnAdd();

        b.Property(e => e.UserId).HasColumnName("user_id");
        b.Property(e => e.TokenHash).HasColumnName("token_hash").IsRequired();
        b.Property(e => e.FamilyId).HasColumnName("family_id").IsRequired();
        b.Property(e => e.ParentTokenId).HasColumnName("parent_token_id");
        b.Property(e => e.DeviceId).HasColumnName("device_id").HasMaxLength(255);
        b.Property(e => e.DeviceName).HasColumnName("device_name").HasMaxLength(200);
        b.Property(e => e.DeviceOs).HasColumnName("device_os").HasMaxLength(50);
        b.Property(e => e.IpAddress).HasColumnName("ip_address").HasColumnType("inet");
        b.Property(e => e.UserAgent).HasColumnName("user_agent");
        b.Property(e => e.IssuedAt).HasColumnName("issued_at").IsRequired();
        b.Property(e => e.ExpiresAt).HasColumnName("expires_at").IsRequired();
        b.Property(e => e.LastUsedAt).HasColumnName("last_used_at");
        b.Property(e => e.RevokedAt).HasColumnName("revoked_at");
        b.Property(e => e.RevokedReason).HasColumnName("revoked_reason").HasMaxLength(50);
        b.Property(e => e.CreatedAt).HasColumnName("created_at").IsRequired();

        b.HasIndex(e => e.TokenHash).IsUnique().HasDatabaseName("refresh_tokens_token_hash_key");

        b.HasOne(e => e.User)
            .WithMany(u => u.RefreshTokens)
            .HasForeignKey(e => e.UserId)
            .OnDelete(DeleteBehavior.Cascade)
            .HasConstraintName("refresh_tokens_user_id_fkey");

        // Self-referential: family_id points to another refresh_token row
        b.HasOne(e => e.Family)
            .WithMany()
            .HasForeignKey(e => e.FamilyId)
            .OnDelete(DeleteBehavior.NoAction)
            .HasConstraintName("refresh_tokens_family_id_fkey");

        b.HasOne(e => e.ParentToken)
            .WithMany()
            .HasForeignKey(e => e.ParentTokenId)
            .OnDelete(DeleteBehavior.NoAction)
            .HasConstraintName("refresh_tokens_parent_token_id_fkey");

        // customer_id FK is cross-BC — not mapped as a navigation.
    }
}
