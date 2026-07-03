using wavio.SharedDataModel.Entities.IdentityAccess;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace wavio.SharedDataModel.Persistence.Configurations.IdentityAccess;

public sealed class OtpCodeConfiguration : IEntityTypeConfiguration<OtpCode>
{
    public void Configure(EntityTypeBuilder<OtpCode> b)
    {
        b.ToTable("otp_codes", "identity_access");

        b.HasKey(e => e.Id);
        b.Property(e => e.Id).HasColumnName("id").ValueGeneratedOnAdd();

        b.Property(e => e.Purpose).HasColumnName("purpose").HasMaxLength(30).IsRequired();
        b.Property(e => e.Identifier).HasColumnName("identifier").HasMaxLength(255).IsRequired();
        b.Property(e => e.IdentifierType).HasColumnName("identifier_type").HasMaxLength(10).IsRequired();
        b.Property(e => e.CodeHash).HasColumnName("code_hash").IsRequired();
        b.Property(e => e.CodeSalt).HasColumnName("code_salt");
        b.Property(e => e.UserId).HasColumnName("user_id");
        b.Property(e => e.ReferenceId).HasColumnName("reference_id");
        b.Property(e => e.ReferenceType).HasColumnName("reference_type").HasMaxLength(50);
        b.Property(e => e.Attempts).HasColumnName("attempts").IsRequired();
        b.Property(e => e.MaxAttempts).HasColumnName("max_attempts").IsRequired();
        b.Property(e => e.VerifiedAt).HasColumnName("verified_at");
        b.Property(e => e.ExpiresAt).HasColumnName("expires_at").IsRequired();
        b.Property(e => e.IpAddress).HasColumnName("ip_address").HasColumnType("inet");
        b.Property(e => e.UserAgent).HasColumnName("user_agent");
        b.Property(e => e.CreatedAt).HasColumnName("created_at").IsRequired();

        b.HasOne(e => e.User)
            .WithMany(u => u.OtpCodes)
            .HasForeignKey(e => e.UserId)
            .OnDelete(DeleteBehavior.Cascade)
            .HasConstraintName("otp_codes_user_id_fkey");

        // customer_id FK points to customer_catalog.customers — NOT mapped here (cross-BC scalar).
    }
}
