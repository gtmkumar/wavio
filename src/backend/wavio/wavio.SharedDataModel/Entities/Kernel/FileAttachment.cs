namespace wavio.SharedDataModel.Entities.Kernel;

/// <summary>Object-storage file reference (kernel.file_attachments).
/// Has created_at, created_by, deleted_at — no updated_at, no version.</summary>
public class FileAttachment
{
    public Guid Id { get; set; }
    public Guid? TenantId { get; set; }
    public string OwnerType { get; set; } = null!;
    public Guid OwnerId { get; set; }
    public string Purpose { get; set; } = null!;
    public string? S3Bucket { get; set; }
    public string S3Key { get; set; } = null!;
    public string StorageProvider { get; set; } = null!;
    public string? CdnUrl { get; set; }
    public string? ThumbnailS3Key { get; set; }
    public string FileName { get; set; } = null!;
    public string MimeType { get; set; } = null!;
    public long Bytes { get; set; }
    public string? Sha256 { get; set; }
    public int? WidthPx { get; set; }
    public int? HeightPx { get; set; }
    public int? DurationSeconds { get; set; }
    public short? PageCount { get; set; }
    public bool IsPublic { get; set; }
    public bool IsEncrypted { get; set; }
    public string? KmsKeyId { get; set; }
    public DateTimeOffset? VirusScannedAt { get; set; }
    public string? VirusScanResult { get; set; }
    public DateTimeOffset? ExpiresAt { get; set; }
    public string? UploadedByType { get; set; }
    public Guid? UploadedById { get; set; }
    public DateTimeOffset UploadedAt { get; set; }
    public DateTimeOffset? LastAccessedAt { get; set; }
    public int AccessCount { get; set; }
    public string Metadata { get; set; } = null!;
    public DateTimeOffset? DeletedAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public Guid? CreatedBy { get; set; }
}
