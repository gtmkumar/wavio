using wavio.SharedDataModel.Entities.Ingest;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace wavio.SharedDataModel.Persistence.Configurations.Ingest;

public sealed class RawWebhookConfiguration : IEntityTypeConfiguration<RawWebhook>
{
    public void Configure(EntityTypeBuilder<RawWebhook> builder)
    {
        builder.ToTable("raw_webhooks", "ingest");

        // Composite PK required by PG range partitioning on received_at (same pattern as AuditLog).
        builder.HasKey(e => new { e.Id, e.ReceivedAt });
        builder.Property(e => e.Id).HasColumnName("id").ValueGeneratedOnAdd();
        builder.Property(e => e.ReceivedAt).HasColumnName("received_at").IsRequired();

        builder.Property(e => e.Source).HasColumnName("source").HasMaxLength(30).IsRequired();
        builder.Property(e => e.TenantId).HasColumnName("tenant_id");
        builder.Property(e => e.SignatureValid).HasColumnName("signature_valid");
        builder.Property(e => e.Headers).HasColumnName("headers").HasColumnType("jsonb");
        builder.Property(e => e.Payload).HasColumnName("payload").HasColumnType("jsonb").IsRequired();
        builder.Property(e => e.ProcessingStatus).HasColumnName("processing_status").HasMaxLength(20).IsRequired();
        builder.Property(e => e.ProcessedAt).HasColumnName("processed_at");
        builder.Property(e => e.ProcessingError).HasColumnName("processing_error");
        builder.Property(e => e.CreatedAt).HasColumnName("created_at").IsRequired();
        builder.Property(e => e.CreatedBy).HasColumnName("created_by");

        // Worker queue scan (crash recovery / replay default range) — mirrors the actual DB
        // index exactly (informational only: this context is database-first, EF never
        // materializes this metadata as DDL).
        builder.HasIndex(e => e.ReceivedAt)
            .HasDatabaseName("raw_webhooks_pending_idx")
            .HasFilter("processing_status = 'received'");
    }
}
