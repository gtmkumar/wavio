using wavio.SharedDataModel.Entities.Messaging;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace wavio.SharedDataModel.Persistence.Configurations.Messaging;

public sealed class CampaignRecipientConfiguration : IEntityTypeConfiguration<CampaignRecipient>
{
    public void Configure(EntityTypeBuilder<CampaignRecipient> builder)
    {
        builder.ToTable("campaign_recipients", "messaging");

        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).HasColumnName("id").ValueGeneratedOnAdd();

        builder.Property(e => e.TenantId).HasColumnName("tenant_id").IsRequired();
        builder.Property(e => e.CampaignId).HasColumnName("campaign_id").IsRequired();
        builder.Property(e => e.WaId).HasColumnName("wa_id").HasMaxLength(20).IsRequired();
        builder.Property(e => e.Params).HasColumnName("params").HasColumnType("jsonb");
        builder.Property(e => e.Status).HasColumnName("status").HasMaxLength(20).IsRequired();
        builder.Property(e => e.OutboundMessageId).HasColumnName("outbound_message_id");
        builder.Property(e => e.ErrorCode).HasColumnName("error_code").HasMaxLength(20);
        builder.Property(e => e.ProcessedAt).HasColumnName("processed_at");

        builder.Property(e => e.CreatedAt).HasColumnName("created_at").IsRequired();
        builder.Property(e => e.UpdatedAt).HasColumnName("updated_at").IsRequired();
        builder.Property(e => e.CreatedBy).HasColumnName("created_by");
        builder.Property(e => e.UpdatedBy).HasColumnName("updated_by");
        builder.Property(e => e.Version).HasColumnName("version").IsRequired();

        builder.HasIndex(e => new { e.CampaignId, e.WaId })
            .HasDatabaseName("campaign_recipients_campaign_id_wa_id_key")
            .IsUnique();
        // Informational only — DDL is canonical (partial index WHERE status = 'pending').
        builder.HasIndex(e => e.CampaignId)
            .HasDatabaseName("campaign_recipients_campaign_pending_idx");
        builder.HasIndex(e => e.TenantId)
            .HasDatabaseName("campaign_recipients_tenant_id_idx");
        builder.HasIndex(e => e.OutboundMessageId)
            .HasDatabaseName("campaign_recipients_outbound_message_id_idx");

        // No navigation property (same "plain POCO" convention as TemplateVersionConfiguration's
        // Template relationship) — but EF still needs to KNOW about the FK so its SaveChanges
        // dependency graph orders new CampaignRecipient rows after their parent Campaign within
        // the same batch. Without this, CreateCampaignCommandHandler's single SaveChangesAsync
        // (one Campaign + N CampaignRecipients) has no guaranteed insert order and can violate
        // campaign_recipients_campaign_id_fkey — confirmed LIVE (not caught by the InMemory
        // provider unit tests use, which never enforces FK constraints at all).
        builder.HasOne<Campaign>().WithMany()
            .HasForeignKey(e => e.CampaignId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
