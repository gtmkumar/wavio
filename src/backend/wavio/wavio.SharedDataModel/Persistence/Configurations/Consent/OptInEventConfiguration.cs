using wavio.SharedDataModel.Entities.Consent;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace wavio.SharedDataModel.Persistence.Configurations.Consent;

public sealed class OptInEventConfiguration : IEntityTypeConfiguration<OptInEvent>
{
    public void Configure(EntityTypeBuilder<OptInEvent> builder)
    {
        builder.ToTable("opt_in_events", "consent");

        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).HasColumnName("id").ValueGeneratedOnAdd();

        builder.Property(e => e.TenantId).HasColumnName("tenant_id").IsRequired();
        builder.Property(e => e.WaId).HasColumnName("wa_id").HasMaxLength(20).IsRequired();
        builder.Property(e => e.Purpose).HasColumnName("purpose").HasMaxLength(20).IsRequired();
        builder.Property(e => e.CaptureChannel).HasColumnName("capture_channel").HasMaxLength(20).IsRequired();
        builder.Property(e => e.Evidence).HasColumnName("evidence").HasColumnType("jsonb");
        builder.Property(e => e.EvidenceWamid).HasColumnName("evidence_wamid").HasMaxLength(128);
        builder.Property(e => e.Actor).HasColumnName("actor").HasMaxLength(120);
        builder.Property(e => e.SourceIp).HasColumnName("source_ip").HasColumnType("inet");
        builder.Property(e => e.OccurredAt).HasColumnName("occurred_at").IsRequired();
        builder.Property(e => e.CreatedAt).HasColumnName("created_at").IsRequired();
        builder.Property(e => e.CreatedBy).HasColumnName("created_by");

        // opt_in_events_tenant_wa_id_occurred_at_idx (V012) is a plain lookup index, not a
        // constraint EF needs to enforce — left unmapped, same convention as
        // OutboundMessageConfiguration's accepted_at index.
    }
}
