using wavio.SharedDataModel.Entities.Consent;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace wavio.SharedDataModel.Persistence.Configurations.Consent;

public sealed class ErasureRequestConfiguration : IEntityTypeConfiguration<ErasureRequest>
{
    public void Configure(EntityTypeBuilder<ErasureRequest> builder)
    {
        builder.ToTable("erasure_requests", "consent");

        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).HasColumnName("id").ValueGeneratedOnAdd();

        builder.Property(e => e.TenantId).HasColumnName("tenant_id").IsRequired();
        builder.Property(e => e.WaId).HasColumnName("wa_id").HasMaxLength(20).IsRequired();
        builder.Property(e => e.RequestType).HasColumnName("request_type").HasMaxLength(20).IsRequired();
        builder.Property(e => e.Status).HasColumnName("status").HasMaxLength(20).IsRequired();
        builder.Property(e => e.RequestedBy).HasColumnName("requested_by").HasMaxLength(120);
        builder.Property(e => e.Reason).HasColumnName("reason");
        builder.Property(e => e.Scope).HasColumnName("scope").HasColumnType("jsonb");
        builder.Property(e => e.ContentErasedAt).HasColumnName("content_erased_at");
        builder.Property(e => e.ExportRef).HasColumnName("export_ref").HasMaxLength(256);
        builder.Property(e => e.CompletedAt).HasColumnName("completed_at");
        builder.Property(e => e.CreatedAt).HasColumnName("created_at").IsRequired();
        builder.Property(e => e.UpdatedAt).HasColumnName("updated_at").IsRequired();
        builder.Property(e => e.CreatedBy).HasColumnName("created_by");
        builder.Property(e => e.UpdatedBy).HasColumnName("updated_by");
        builder.Property(e => e.Version).HasColumnName("version").IsRequired();

        // erasure_requests_tenant_wa_id_idx / erasure_requests_status_idx (V012) are plain/partial
        // lookup indexes, not constraints EF needs to enforce — left unmapped, same convention as
        // OutboundMessageConfiguration's accepted_at index.
    }
}
