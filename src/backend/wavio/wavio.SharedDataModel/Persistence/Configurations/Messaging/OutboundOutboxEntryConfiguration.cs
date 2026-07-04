using wavio.SharedDataModel.Entities.Messaging;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace wavio.SharedDataModel.Persistence.Configurations.Messaging;

public sealed class OutboundOutboxEntryConfiguration : IEntityTypeConfiguration<OutboundOutboxEntry>
{
    public void Configure(EntityTypeBuilder<OutboundOutboxEntry> builder)
    {
        builder.ToTable("outbound_outbox", "messaging");

        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).HasColumnName("id").ValueGeneratedOnAdd();

        builder.Property(e => e.TenantId).HasColumnName("tenant_id").IsRequired();
        builder.Property(e => e.OutboundMessageId).HasColumnName("outbound_message_id").IsRequired();
        builder.Property(e => e.PhoneNumberId).HasColumnName("phone_number_id").IsRequired();
        builder.Property(e => e.Status).HasColumnName("status").HasMaxLength(20).IsRequired();
        builder.Property(e => e.Attempts).HasColumnName("attempts").IsRequired();
        builder.Property(e => e.MaxAttempts).HasColumnName("max_attempts").IsRequired();
        builder.Property(e => e.NextAttemptAt).HasColumnName("next_attempt_at").IsRequired();
        builder.Property(e => e.LockedBy).HasColumnName("locked_by").HasMaxLength(100);
        builder.Property(e => e.LockedAt).HasColumnName("locked_at");
        builder.Property(e => e.LastErrorCode).HasColumnName("last_error_code").HasMaxLength(20);
        builder.Property(e => e.LastError).HasColumnName("last_error");

        builder.Property(e => e.CreatedAt).HasColumnName("created_at").IsRequired();
        builder.Property(e => e.UpdatedAt).HasColumnName("updated_at").IsRequired();
        builder.Property(e => e.CreatedBy).HasColumnName("created_by");
        builder.Property(e => e.UpdatedBy).HasColumnName("updated_by");

        builder.HasIndex(e => e.OutboundMessageId)
            .IsUnique()
            .HasDatabaseName("outbound_outbox_outbound_message_id_key");
        builder.HasIndex(e => e.NextAttemptAt)
            .HasDatabaseName("outbound_outbox_due_idx");
        builder.HasIndex(e => e.PhoneNumberId)
            .HasDatabaseName("outbound_outbox_phone_number_id_idx");
        builder.HasIndex(e => e.TenantId)
            .HasDatabaseName("outbound_outbox_tenant_id_idx");
    }
}
