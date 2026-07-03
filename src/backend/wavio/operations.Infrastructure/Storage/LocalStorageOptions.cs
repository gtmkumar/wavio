namespace operations.Infrastructure.Storage;

/// <summary>
/// Configuration options for <see cref="LocalFileStorageProvider"/>.
/// Bound from the <c>Storage:Local</c> config section.
/// </summary>
public sealed class LocalStorageOptions
{
    /// <summary>
    /// Root directory for local file storage.
    /// Default: <c>/tmp/wavio-uploads</c>. Override via <c>Storage:Local:RootPath</c>.
    ///
    /// In Development it is safe to use a local path; in Production use
    /// <c>Storage:Provider = s3</c> or <c>azure-blob</c> instead.
    /// </summary>
    public string RootPath { get; set; } = "/tmp/wavio-uploads";
}
