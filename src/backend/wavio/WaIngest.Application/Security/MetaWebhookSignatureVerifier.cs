using System.Security.Cryptography;
using System.Text;

namespace WaIngest.Application.Security;

/// <summary>
/// Verifies Meta's <c>X-Hub-Signature-256</c> webhook signature (spec §4.3, §5): HMAC-SHA256 of
/// the exact raw request body, keyed by the Meta App Secret, hex-encoded and prefixed
/// <c>sha256=</c>. Pure/static and dependency-free so it is trivially unit-testable without a
/// running host — verification MUST happen against the raw bytes, before any JSON parsing
/// (re-serializing would not reproduce Meta's exact byte stream and would always mismatch).
/// </summary>
public static class MetaWebhookSignatureVerifier
{
    private const string SignaturePrefix = "sha256=";

    /// <summary>
    /// Returns true only when <paramref name="signatureHeader"/> is present, well-formed, and
    /// matches the HMAC-SHA256 of <paramref name="rawBody"/> keyed by
    /// <paramref name="appSecret"/>. Comparison is constant-time to avoid a timing oracle on the
    /// signature. Missing header, empty secret, or a malformed header all fail closed (false) —
    /// callers must reject with 401 and skip further processing on any false result.
    /// </summary>
    public static bool Verify(ReadOnlySpan<byte> rawBody, string? signatureHeader, string appSecret)
    {
        if (string.IsNullOrEmpty(appSecret)) return false;
        if (string.IsNullOrWhiteSpace(signatureHeader)) return false;
        if (!signatureHeader.StartsWith(SignaturePrefix, StringComparison.Ordinal)) return false;

        var providedHex = signatureHeader.AsSpan(SignaturePrefix.Length);

        Span<byte> provided = stackalloc byte[32]; // SHA-256 digest length
        if (!TryParseHex(providedHex, provided)) return false;

        Span<byte> computed = stackalloc byte[32];
        var keyBytes = Encoding.UTF8.GetBytes(appSecret);
        if (!HMACSHA256.TryHashData(keyBytes, rawBody, computed, out var written) || written != 32)
            return false;

        // Constant-time compare — never short-circuit on the first differing byte.
        return CryptographicOperations.FixedTimeEquals(provided, computed);
    }

    private static bool TryParseHex(ReadOnlySpan<char> hex, Span<byte> destination)
    {
        if (hex.Length != destination.Length * 2) return false;

        for (var i = 0; i < destination.Length; i++)
        {
            var hi = HexDigit(hex[i * 2]);
            var lo = HexDigit(hex[i * 2 + 1]);
            if (hi < 0 || lo < 0) return false;
            destination[i] = (byte)((hi << 4) | lo);
        }

        return true;
    }

    private static int HexDigit(char c) => c switch
    {
        >= '0' and <= '9' => c - '0',
        >= 'a' and <= 'f' => c - 'a' + 10,
        >= 'A' and <= 'F' => c - 'A' + 10,
        _ => -1
    };
}
