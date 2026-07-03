using wavio.SharedDataModel.Entities.Messaging;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace wavio.SharedDataModel.Persistence.Configurations.Messaging;

public sealed class OutboundMessageConfiguration : IEntityTypeConfiguration<OutboundMessage>
{
    public void Configure(EntityTypeBuilder<OutboundMessage> builder)
    {
        builder.ToTable("outbound_messages", "messaging");

        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).HasColumnName("id").ValueGeneratedOnAdd();

        builder.Property(e => e.TenantId).HasColumnName("tenant_id").IsRequired();
        builder.Property(e => e.PhoneNumberId).HasColumnName("phone_number_id").IsRequired();
        builder.Property(e => e.ToWaId).HasColumnName("to_wa_id").HasMaxLength(20).IsRequired();
        builder.Property(e => e.MessageType).HasColumnName("message_type").HasMaxLength(30).IsRequired();
        builder.Property(e => e.TemplateId).HasColumnName("template_id");
        builder.Property(e => e.TemplateVersionId).HasColumnName("template_version_id");
        builder.Property(e => e.Payload).HasColumnName("payload").HasColumnType("jsonb").IsRequired();
        builder.Property(e => e.IdempotencyKey).HasColumnName("idempotency_key").HasMaxLength(100).IsRequired();
        builder.Property(e => e.IdempotencyActive).HasColumnName("idempotency_active").IsRequired();
        builder.Property(e => e.Status).HasColumnName("status").HasMaxLength(20).IsRequired();
        builder.Property(e => e.Wamid).HasColumnName("wamid").HasMaxLength(128);
        builder.Property(e => e.BillableEstimate).HasColumnName("billable_estimate");
        builder.Property(e => e.ErrorCode).HasColumnName("error_code").HasMaxLength(20);
        builder.Property(e => e.ErrorMessage).HasColumnName("error_message");
        builder.Property(e => e.AcceptedAt).HasColumnName("accepted_at").IsRequired();
        builder.Property(e => e.DispatchedAt).HasColumnName("dispatched_at");

        builder.Property(e => e.CreatedAt).HasColumnName("created_at").IsRequired();
        builder.Property(e => e.UpdatedAt).HasColumnName("updated_at").IsRequired();
        builder.Property(e => e.CreatedBy).HasColumnName("created_by");
        builder.Property(e => e.UpdatedBy).HasColumnName("updated_by");
        builder.Property(e => e.Version).HasColumnName("version").IsRequired();

        // Informational only — DDL is canonical (partial unique WHERE idempotency_active).
        builder.HasIndex(e => new { e.TenantId, e.IdempotencyKey })
            .HasDatabaseName("outbound_messages_tenant_id_idempotency_key_key");
        builder.HasIndex(e => e.Wamid)
            .HasDatabaseName("outbound_messages_wamid_key");
    }
}
