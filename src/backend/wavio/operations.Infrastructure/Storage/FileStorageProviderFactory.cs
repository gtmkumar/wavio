using Microsoft.Extensions.Configuration;

namespace operations.Infrastructure.Storage;

/// <summary>
/// Reads <c>Storage:Provider</c> from configuration and selects the matching
/// <see cref="operations.Application.Common.Interfaces.IFileStorageProvider"/> registration.
///
/// <para>Supported values for <c>Storage:Provider</c>: <c>local</c> (default), <c>s3</c>,
/// <c>azure-blob</c>. Cloud providers are seams that throw until wired.</para>
/// </summary>
internal static class FileStorageProviderFactory
{
    private const string ProviderKey = "Storage:Provider";

    public static string ResolveProviderName(IConfiguration configuration)
    {
        var name = configuration[ProviderKey]?.Trim().ToLowerInvariant() ?? "local";

        return name switch
        {
            "local" or "" => "local",

            // ── Cloud provider seams ──────────────────────────────────────────
            "s3" => throw new NotSupportedException(
                "Storage:Provider 's3' is not yet wired. " +
                "Add AWSSDK.S3, create S3FileStorageProvider, and register it in StorageRegistration."),

            "azure-blob" => throw new NotSupportedException(
                "Storage:Provider 'azure-blob' is not yet wired. " +
                "Add Azure.Storage.Blobs, create AzureBlobStorageProvider, and register it in StorageRegistration."),

            _ => throw new NotSupportedException(
                $"Unknown Storage:Provider value '{name}'. " +
                "Supported values: local (default), s3, azure-blob.")
        };
    }
}
