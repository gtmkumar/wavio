using wavio.SharedDataModel.Entities.Kernel;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace wavio.SharedDataModel.Persistence.Configurations.Kernel;

public sealed class FileAttachmentConfiguration : IEntityTypeConfiguration<FileAttachment>
{
    public void Configure(EntityTypeBuilder<FileAttachment> b)
    {
        b.ToTable("file_attachments", "kernel");

        b.HasKey(e => e.Id);
        b.Property(e => e.Id).HasColumnName("id").ValueGeneratedOnAdd();

        b.Property(e => e.TenantId).HasColumnName("tenant_id");
        b.Property(e => e.OwnerType).HasColumnName("owner_type").HasMaxLength(50).IsRequired();
        b.Property(e => e.OwnerId).HasColumnName("owner_id").IsRequired();
        b.Property(e => e.Purpose).HasColumnName("purpose").HasMaxLength(50).IsRequired();
        b.Property(e => e.S3Bucket).HasColumnName("s3_bucket").HasMaxLength(100);
        b.Property(e => e.S3Key).HasColumnName("s3_key").IsRequired();
        b.Property(e => e.StorageProvider).HasColumnName("storage_provider").HasMaxLength(20).IsRequired();
        b.Property(e => e.CdnUrl).HasColumnName("cdn_url");
        b.Property(e => e.ThumbnailS3Key).HasColumnName("thumbnail_s3_key");
        b.Property(e => e.FileName).HasColumnName("file_name").HasMaxLength(500).IsRequired();
        b.Property(e => e.MimeType).HasColumnName("mime_type").HasMaxLength(100).IsRequired();
        b.Property(e => e.Bytes).HasColumnName("bytes").IsRequired();
        b.Property(e => e.Sha256).HasColumnName("sha256").HasColumnType("character(64)");
        b.Property(e => e.WidthPx).HasColumnName("width_px");
        b.Property(e => e.HeightPx).HasColumnName("height_px");
        b.Property(e => e.DurationSeconds).HasColumnName("duration_seconds");
        b.Property(e => e.PageCount).HasColumnName("page_count");
        b.Property(e => e.IsPublic).HasColumnName("is_public").IsRequired();
        b.Property(e => e.IsEncrypted).HasColumnName("is_encrypted").IsRequired();
        b.Property(e => e.KmsKeyId).HasColumnName("kms_key_id").HasMaxLength(200);
        b.Property(e => e.VirusScannedAt).HasColumnName("virus_scanned_at");
        b.Property(e => e.VirusScanResult).HasColumnName("virus_scan_result").HasMaxLength(20);
        b.Property(e => e.ExpiresAt).HasColumnName("expires_at");
        b.Property(e => e.UploadedByType).HasColumnName("uploaded_by_type").HasMaxLength(20);
        b.Property(e => e.UploadedById).HasColumnName("uploaded_by_id");
        b.Property(e => e.UploadedAt).HasColumnName("uploaded_at").IsRequired();
        b.Property(e => e.LastAccessedAt).HasColumnName("last_accessed_at");
        b.Property(e => e.AccessCount).HasColumnName("access_count").IsRequired();
        b.Property(e => e.Metadata).HasColumnName("metadata").HasColumnType("jsonb").IsRequired();
        b.Property(e => e.DeletedAt).HasColumnName("deleted_at");
        b.Property(e => e.CreatedAt).HasColumnName("created_at").IsRequired();
        b.Property(e => e.CreatedBy).HasColumnName("created_by");

        // Soft-delete query filter
        b.HasQueryFilter(e => e.DeletedAt == null);
    }
}
