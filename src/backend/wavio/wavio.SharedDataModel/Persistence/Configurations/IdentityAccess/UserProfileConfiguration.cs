using wavio.SharedDataModel.Crypto;
using wavio.SharedDataModel.Entities.IdentityAccess;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace wavio.SharedDataModel.Persistence.Configurations.IdentityAccess;

public sealed class UserProfileConfiguration : IEntityTypeConfiguration<UserProfile>
{
    public void Configure(EntityTypeBuilder<UserProfile> b)
    {
        b.ToTable("user_profiles", "identity_access");

        // PK is the FK itself (1-to-1)
        b.HasKey(e => e.UserId);
        b.Property(e => e.UserId).HasColumnName("user_id").ValueGeneratedNever();

        b.Property(e => e.FirstName).HasColumnName("first_name").HasMaxLength(100);
        b.Property(e => e.LastName).HasColumnName("last_name").HasMaxLength(100);
        b.Property(e => e.DisplayName).HasColumnName("display_name").HasMaxLength(200);
        b.Property(e => e.AvatarUrl).HasColumnName("avatar_url");
        b.Property(e => e.DateOfBirth).HasColumnName("date_of_birth");
        b.Property(e => e.Gender).HasColumnName("gender").HasMaxLength(20);
        b.Property(e => e.Designation).HasColumnName("designation").HasMaxLength(100);
        b.Property(e => e.Department).HasColumnName("department").HasMaxLength(100);
        b.Property(e => e.EmployeeId).HasColumnName("employee_id").HasMaxLength(50);
        b.Property(e => e.JoinedAt).HasColumnName("joined_at");
        b.Property(e => e.EmergencyContactName).HasColumnName("emergency_contact_name").HasMaxLength(200);
        b.Property(e => e.EmergencyContactPhone).HasColumnName("emergency_contact_phone").HasMaxLength(20);
        b.Property(e => e.Address).HasColumnName("address").HasColumnType("jsonb");
        b.Property(e => e.EmploymentType).HasColumnName("employment_type").HasMaxLength(20);
        b.Property(e => e.AadhaarNumberMasked).HasColumnName("aadhaar_number_masked").HasMaxLength(20);
        b.Property(e => e.KycStatus).HasColumnName("kyc_status").HasMaxLength(20);
        b.Property(e => e.KycVerifiedAt).HasColumnName("kyc_verified_at");
        b.Property(e => e.BankAccountName).HasColumnName("bank_account_name").HasMaxLength(200);

        // ── PII columns: AES-256-GCM encrypted at rest ──────────────────────────
        // Column type is widened to text (no MaxLength) to accommodate base64 ciphertext.
        // PiiValueConverter.Instance is null-safe: if the cipher was not configured
        // (only possible in test harnesses that don't call AddSharedDataModel), the
        // converter is not applied and the raw value is stored — acceptable for unit
        // tests that don't exercise encryption.
        if (PiiValueConverter.Instance is { } conv)
        {
            b.Property(e => e.PanNumber)
                .HasColumnName("pan_number")
                .HasConversion(conv);
            b.Property(e => e.BankAccountNumber)
                .HasColumnName("bank_account_number")
                .HasConversion(conv);
            b.Property(e => e.UpiId)
                .HasColumnName("upi_id")
                .HasConversion(conv);
        }
        else
        {
            b.Property(e => e.PanNumber).HasColumnName("pan_number").HasMaxLength(10);
            b.Property(e => e.BankAccountNumber).HasColumnName("bank_account_number").HasMaxLength(50);
            b.Property(e => e.UpiId).HasColumnName("upi_id").HasMaxLength(100);
        }

        // IFSC: branch code, publicly listed, not encrypted.
        b.Property(e => e.BankIfsc).HasColumnName("bank_ifsc").HasMaxLength(11);
        b.Property(e => e.FcmToken).HasColumnName("fcm_token");
        b.Property(e => e.FcmTokenUpdatedAt).HasColumnName("fcm_token_updated_at");
        b.Property(e => e.ApnsToken).HasColumnName("apns_token");
        b.Property(e => e.ApnsTokenUpdatedAt).HasColumnName("apns_token_updated_at");
        b.Property(e => e.Preferences).HasColumnName("preferences").HasColumnType("jsonb").IsRequired();
        b.Property(e => e.Metadata).HasColumnName("metadata").HasColumnType("jsonb").IsRequired();
        b.Property(e => e.CreatedAt).HasColumnName("created_at").IsRequired();
        b.Property(e => e.UpdatedAt).HasColumnName("updated_at").IsRequired();
        b.Property(e => e.CreatedBy).HasColumnName("created_by");
        b.Property(e => e.UpdatedBy).HasColumnName("updated_by");
        b.Property(e => e.Status).HasColumnName("status").HasMaxLength(20).IsRequired();

        b.HasOne(e => e.User)
            .WithOne(u => u.Profile)
            .HasForeignKey<UserProfile>(e => e.UserId)
            .OnDelete(DeleteBehavior.Cascade)
            .HasConstraintName("user_profiles_user_id_fkey");
    }
}
