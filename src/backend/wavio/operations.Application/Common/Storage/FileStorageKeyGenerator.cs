using System.Text.RegularExpressions;

namespace operations.Application.Common.Storage;

/// <summary>
/// Generates server-side storage keys in the canonical scheme:
/// <code>
///   {tenantId:N}/{area}/{uuid:N}.{ext}
/// </code>
/// Keys are always server-generated — callers supply a MIME type and an area label.
/// The extension is derived from the MIME type; client-supplied file names are never used.
/// This prevents path-traversal (no <c>..</c> segments, no absolute paths) and
/// filename injection (no user-controlled characters in the key).
///
/// <para>Pure, dependency-free helper — lives in operations.Application so both any
/// query handler (extension resolution) and the Infrastructure provider can share it.</para>
/// </summary>
public static class FileStorageKeyGenerator
{
    // Must start with a lowercase letter or digit (no leading slash or hyphen),
    // then allow lowercase letters, digits, underscores, forward slashes, and hyphens.
    private static readonly Regex SafeAreaPattern = new(@"^[a-z0-9][a-z0-9_/\-]{0,63}$", RegexOptions.Compiled);

    private static readonly Dictionary<string, string> MimeToExt = new(StringComparer.OrdinalIgnoreCase)
    {
        ["image/jpeg"]  = "jpg",
        ["image/jpg"]   = "jpg",
        ["image/png"]   = "png",
        ["image/webp"]  = "webp",
        ["image/gif"]   = "gif",
        ["application/pdf"] = "pdf",
        ["text/csv"]    = "csv",
    };

    /// <summary>
    /// Generates a path-traversal-safe storage key.
    /// </summary>
    /// <param name="tenantId">Tenant — forms the first path segment.</param>
    /// <param name="area">Purpose label (e.g. <c>attachments</c>, <c>avatars</c>).
    /// Must match <c>[a-z0-9_/-]{1,64}</c>.</param>
    /// <param name="contentType">MIME type — used to derive the file extension.</param>
    /// <exception cref="ArgumentException">When <paramref name="area"/> contains unsafe characters.</exception>
    public static string Generate(Guid tenantId, string area, string contentType)
    {
        if (!SafeAreaPattern.IsMatch(area))
            throw new ArgumentException(
                $"Area '{area}' contains characters outside [a-z0-9_/-] or exceeds 64 chars.", nameof(area));

        var ext = ResolveExtension(contentType);
        var id  = Guid.NewGuid().ToString("N");
        var tenant = tenantId.ToString("N");

        return $"{tenant}/{area}/{id}.{ext}";
    }

    /// <summary>
    /// Returns the file extension for a given MIME type. Returns <c>bin</c> as a safe fallback
    /// for unrecognised types — the MIME type stored in the DB is the authoritative content-type.
    /// </summary>
    public static string ResolveExtension(string contentType)
    {
        var mimeBase = contentType.Split(';')[0].Trim();
        return MimeToExt.TryGetValue(mimeBase, out var ext) ? ext : "bin";
    }
}
