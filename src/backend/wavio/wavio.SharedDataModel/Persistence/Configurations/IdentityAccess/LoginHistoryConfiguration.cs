using wavio.SharedDataModel.Entities.IdentityAccess;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace wavio.SharedDataModel.Persistence.Configurations.IdentityAccess;

public sealed class LoginHistoryConfiguration : IEntityTypeConfiguration<LoginHistory>
{
    public void Configure(EntityTypeBuilder<LoginHistory> b)
    {
        b.ToTable("login_history", "identity_access");

        b.HasKey(e => e.Id);
        b.Property(e => e.Id).HasColumnName("id").ValueGeneratedOnAdd();

        b.Property(e => e.UserId).HasColumnName("user_id");
        b.Property(e => e.Identifier).HasColumnName("identifier").HasMaxLength(255).IsRequired();
        b.Property(e => e.AuthMethod).HasColumnName("auth_method").HasMaxLength(20).IsRequired();
        b.Property(e => e.Success).HasColumnName("success").IsRequired();
        b.Property(e => e.FailureReason).HasColumnName("failure_reason").HasMaxLength(100);
        b.Property(e => e.IpAddress).HasColumnName("ip_address").HasColumnType("inet");
        b.Property(e => e.UserAgent).HasColumnName("user_agent");
        b.Property(e => e.DeviceId).HasColumnName("device_id").HasMaxLength(255);
        b.Property(e => e.CountryCode).HasColumnName("country_code").HasColumnType("character(2)");
        b.Property(e => e.City).HasColumnName("city").HasMaxLength(100);
        b.Property(e => e.IsSuspicious).HasColumnName("is_suspicious").IsRequired();
        b.Property(e => e.RiskScore).HasColumnName("risk_score");
        b.Property(e => e.OccurredAt).HasColumnName("occurred_at").IsRequired();
        b.Property(e => e.CreatedAt).HasColumnName("created_at").IsRequired();
        b.Property(e => e.CreatedBy).HasColumnName("created_by");

        b.HasOne(e => e.User)
            .WithMany(u => u.LoginHistories)
            .HasForeignKey(e => e.UserId)
            .OnDelete(DeleteBehavior.SetNull)
            .HasConstraintName("login_history_user_id_fkey");

        // customer_id FK is cross-BC — not mapped as navigation.
    }
}
