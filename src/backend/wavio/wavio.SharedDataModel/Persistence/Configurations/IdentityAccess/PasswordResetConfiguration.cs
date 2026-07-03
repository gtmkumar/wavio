using wavio.SharedDataModel.Entities.IdentityAccess;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace wavio.SharedDataModel.Persistence.Configurations.IdentityAccess;

public sealed class PasswordResetConfiguration : IEntityTypeConfiguration<PasswordReset>
{
    public void Configure(EntityTypeBuilder<PasswordReset> b)
    {
        b.ToTable("password_resets", "identity_access");

        b.HasKey(e => e.Id);
        b.Property(e => e.Id).HasColumnName("id").ValueGeneratedOnAdd();

        b.Property(e => e.UserId).HasColumnName("user_id");
        b.Property(e => e.TokenHash).HasColumnName("token_hash").IsRequired();
        b.Property(e => e.RequestedIp).HasColumnName("requested_ip").HasColumnType("inet");
        b.Property(e => e.RequestedUserAgent).HasColumnName("requested_user_agent");
        b.Property(e => e.UsedAt).HasColumnName("used_at");
        b.Property(e => e.UsedIp).HasColumnName("used_ip").HasColumnType("inet");
        b.Property(e => e.ExpiresAt).HasColumnName("expires_at").IsRequired();
        b.Property(e => e.CreatedAt).HasColumnName("created_at").IsRequired();
        b.Property(e => e.UpdatedAt).HasColumnName("updated_at").IsRequired();
        b.Property(e => e.CreatedBy).HasColumnName("created_by");
        b.Property(e => e.UpdatedBy).HasColumnName("updated_by");
        b.Property(e => e.Status).HasColumnName("status").HasMaxLength(20).IsRequired();

        b.HasIndex(e => e.TokenHash).IsUnique().HasDatabaseName("password_resets_token_hash_key");

        b.HasOne(e => e.User)
            .WithMany(u => u.PasswordResets)
            .HasForeignKey(e => e.UserId)
            .OnDelete(DeleteBehavior.Cascade)
            .HasConstraintName("password_resets_user_id_fkey");

        // customer_id FK is cross-BC — not mapped as navigation.
    }
}
