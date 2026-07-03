using wavio.SharedDataModel.Entities.IdentityAccess;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace wavio.SharedDataModel.Persistence.Configurations.IdentityAccess;

public sealed class UserConfiguration : IEntityTypeConfiguration<User>
{
    public void Configure(EntityTypeBuilder<User> b)
    {
        b.ToTable("users", "identity_access");

        b.HasKey(e => e.Id);
        b.Property(e => e.Id).HasColumnName("id").ValueGeneratedOnAdd();

        b.Property(e => e.PhoneE164).HasColumnName("phone_e164").HasMaxLength(20);
        b.Property(e => e.Email).HasColumnName("email").HasColumnType("citext");
        b.Property(e => e.PasswordHash).HasColumnName("password_hash");
        b.Property(e => e.PasswordChangedAt).HasColumnName("password_changed_at");
        b.Property(e => e.MustChangePassword).HasColumnName("must_change_password").IsRequired();
        b.Property(e => e.MfaEnabled).HasColumnName("mfa_enabled").IsRequired();
        b.Property(e => e.MfaSecret).HasColumnName("mfa_secret");
        b.Property(e => e.MfaBackupCodes).HasColumnName("mfa_backup_codes").HasColumnType("text[]");
        b.Property(e => e.UserType).HasColumnName("user_type").HasMaxLength(30).IsRequired();
        b.Property(e => e.Locale).HasColumnName("locale").HasMaxLength(10).IsRequired();
        b.Property(e => e.Timezone).HasColumnName("timezone").HasMaxLength(50).IsRequired();
        b.Property(e => e.Status).HasColumnName("status").HasMaxLength(20).IsRequired();
        b.Property(e => e.LastLoginAt).HasColumnName("last_login_at");
        b.Property(e => e.LastLoginIp).HasColumnName("last_login_ip").HasColumnType("inet");
        b.Property(e => e.LastActiveAt).HasColumnName("last_active_at");
        b.Property(e => e.FailedAttempts).HasColumnName("failed_attempts").IsRequired();
        b.Property(e => e.LockedUntil).HasColumnName("locked_until");
        b.Property(e => e.EmailVerifiedAt).HasColumnName("email_verified_at");
        b.Property(e => e.PhoneVerifiedAt).HasColumnName("phone_verified_at");
        b.Property(e => e.InvitationToken).HasColumnName("invitation_token");
        b.Property(e => e.InvitationSentAt).HasColumnName("invitation_sent_at");
        b.Property(e => e.InvitationAcceptedAt).HasColumnName("invitation_accepted_at");
        b.Property(e => e.CreatedAt).HasColumnName("created_at").IsRequired();
        b.Property(e => e.UpdatedAt).HasColumnName("updated_at").IsRequired();
        b.Property(e => e.CreatedBy).HasColumnName("created_by");
        b.Property(e => e.UpdatedBy).HasColumnName("updated_by");
        b.Property(e => e.Version).HasColumnName("version").IsRequired();
        b.Property(e => e.PermVersion).HasColumnName("perm_version").IsRequired();
        b.Property(e => e.DeletedAt).HasColumnName("deleted_at");

        b.HasIndex(e => e.Email).IsUnique().HasDatabaseName("users_email_key");
        b.HasIndex(e => e.PhoneE164).IsUnique().HasDatabaseName("users_phone_e164_key");

        b.HasQueryFilter(e => e.DeletedAt == null);
    }
}
