using wavio.SharedDataModel.Entities.Consent;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace wavio.SharedDataModel.Persistence.Configurations.Consent;

public sealed class OptOutEventConfiguration : IEntityTypeConfiguration<OptOutEvent>
{
    public void Configure(EntityTypeBuilder<OptOutEvent> builder)
    {
        builder.ToTable("opt_out_events", "consent");

        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).HasColumnName("id").ValueGeneratedOnAdd();

        builder.Property(e => e.TenantId).HasColumnName("tenant_id").IsRequired();
        builder.Property(e => e.WaId).HasColumnName("wa_id").HasMaxLength(20).IsRequired();
        builder.Property(e => e.Scope).HasColumnName("scope").HasMaxLength(20).IsRequired();
        builder.Property(e => e.Reason).HasColumnName("reason").HasMaxLength(30).IsRequired();
        builder.Property(e => e.Keyword).HasColumnName("keyword").HasMaxLength(40);
        builder.Property(e => e.Language).HasColumnName("language").HasMaxLength(15);
        builder.Property(e => e.InboundWamid).HasColumnName("inbound_wamid").HasMaxLength(128);
        builder.Property(e => e.Payload).HasColumnName("payload").HasColumnType("jsonb");
        builder.Property(e => e.OccurredAt).HasColumnName("occurred_at").IsRequired();
        builder.Property(e => e.CreatedAt).HasColumnName("created_at").IsRequired();
        builder.Property(e => e.CreatedBy).HasColumnName("created_by");

        // opt_out_events_tenant_wa_id_occurred_at_idx (V012) is a plain lookup index, not a
        // constraint EF needs to enforce — left unmapped, same convention as
        // OutboundMessageConfiguration's accepted_at index.
    }
}
