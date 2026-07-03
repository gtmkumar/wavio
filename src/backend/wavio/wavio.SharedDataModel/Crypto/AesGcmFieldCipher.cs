using System.Security.Cryptography;
using System.Text;

namespace wavio.SharedDataModel.Crypto;

/// <summary>
/// AES-256-GCM field cipher.
///
/// Wire format (inside the base64 payload):
///   [12-byte nonce][16-byte GCM tag][ciphertext bytes]
///
/// Stored value format (what goes into the DB column):
///   "enc:v1:" + Base64(nonce + tag + ciphertext)
///
/// The "enc:v1:" prefix is used by <see cref="PiiValueConverter"/> to distinguish
/// encrypted rows from legacy plaintext rows written before encryption was enabled.
/// Legacy rows are returned as-is on reads and re-encrypted transparently on the next write.
/// </summary>
public sealed class AesGcmFieldCipher : IFieldCipher
{
    /// <summary>Prefix written before every encrypted ciphertext column value.</summary>
    internal const string Prefix = "enc:v1:";

    private const int NonceSize    = 12; // AES-GCM recommended nonce size
    private const int TagSize      = 16; // 128-bit authentication tag
    private const int KeySizeBytes = 32; // AES-256

    private readonly byte[] _key;

    public AesGcmFieldCipher(byte[] key)
    {
        if (key.Length != KeySizeBytes)
            throw new ArgumentException(
                $"Pii:EncryptionKey must be exactly {KeySizeBytes} bytes (256 bits) after base64 decoding. " +
                $"Got {key.Length} bytes.", nameof(key));
        _key = key;
    }

    /// <inheritdoc />
    public string? Encrypt(string? plaintext)
    {
        if (plaintext is null) return null;

        var nonce      = RandomNumberGenerator.GetBytes(NonceSize);
        var tag        = new byte[TagSize];
        var plainBytes = Encoding.UTF8.GetBytes(plaintext);
        var cipherBytes = new byte[plainBytes.Length];

        using var aes = new AesGcm(_key, TagSize);
        aes.Encrypt(nonce, plainBytes, cipherBytes, tag);

        // Pack as: nonce || tag || ciphertext → base64
        var payload = new byte[NonceSize + TagSize + cipherBytes.Length];
        nonce.CopyTo(payload, 0);
        tag.CopyTo(payload, NonceSize);
        cipherBytes.CopyTo(payload, NonceSize + TagSize);

        return Prefix + Convert.ToBase64String(payload);
    }

    /// <inheritdoc />
    public string? Decrypt(string? value)
    {
        if (value is null) return null;

        // Legacy plaintext passthrough: if the value doesn't carry our prefix, it was
        // written before encryption was enabled. Return as-is so existing rows keep working.
        // The next write through the EF converter will silently re-encrypt the value.
        if (!value.StartsWith(Prefix, StringComparison.Ordinal))
            return value;

        var payload = Convert.FromBase64String(value[Prefix.Length..]);

        if (payload.Length < NonceSize + TagSize)
            throw new CryptographicException(
                "PII field ciphertext payload is too short — data may be corrupted.");

        var nonce      = payload[..NonceSize];
        var tag        = payload[NonceSize..(NonceSize + TagSize)];
        var cipherBytes = payload[(NonceSize + TagSize)..];
        var plainBytes  = new byte[cipherBytes.Length];

        using var aes = new AesGcm(_key, TagSize);
        aes.Decrypt(nonce, cipherBytes, tag, plainBytes);

        return Encoding.UTF8.GetString(plainBytes);
    }
}
