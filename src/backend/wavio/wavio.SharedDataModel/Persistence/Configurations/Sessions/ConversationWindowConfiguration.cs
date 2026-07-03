using wavio.SharedDataModel.Entities.Sessions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace wavio.SharedDataModel.Persistence.Configurations.Sessions;

public sealed class ConversationWindowConfiguration : IEntityTypeConfiguration<ConversationWindow>
{
    public void Configure(EntityTypeBuilder<ConversationWindow> builder)
    {
        builder.ToTable("conversation_windows", "sessions");

        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).HasColumnName("id").ValueGeneratedOnAdd();

        builder.Property(e => e.TenantId).HasColumnName("tenant_id").IsRequired();
        builder.Property(e => e.PhoneNumberId).HasColumnName("phone_number_id").IsRequired();
        builder.Property(e => e.UserWaId).HasColumnName("user_wa_id").HasMaxLength(20).IsRequired();
        builder.Property(e => e.Origin).HasColumnName("origin").HasMaxLength(10).IsRequired();

        builder.Property(e => e.CsExpiresAt).HasColumnName("cs_expires_at");
        builder.Property(e => e.CsLastInboundAt).HasColumnName("cs_last_inbound_at");
        builder.Property(e => e.CtwaExpiresAt).HasColumnName("ctwa_expires_at");
        builder.Property(e => e.CtwaEnteredAt).HasColumnName("ctwa_entered_at");
        builder.Property(e => e.CtwaReferral).HasColumnName("ctwa_referral").HasColumnType("jsonb");
        builder.Property(e => e.ClosingNotifiedAt).HasColumnName("closing_notified_at");
        builder.Property(e => e.IsSimulated).HasColumnName("is_simulated").IsRequired();

        builder.Property(e => e.CreatedAt).HasColumnName("created_at").IsRequired();
        builder.Property(e => e.UpdatedAt).HasColumnName("updated_at").IsRequired();
        builder.Property(e => e.CreatedBy).HasColumnName("created_by");
        builder.Property(e => e.UpdatedBy).HasColumnName("updated_by");
        builder.Property(e => e.Version).HasColumnName("version").IsRequired();

        // The UPSERT target — single active row per pair (db/migrations/V008).
        builder.HasIndex(e => new { e.TenantId, e.PhoneNumberId, e.UserWaId })
            .IsUnique()
            .HasDatabaseName("conversation_windows_pair_key");
    }
}
