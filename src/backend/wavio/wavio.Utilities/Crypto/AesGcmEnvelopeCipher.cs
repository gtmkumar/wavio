using System.Security.Cryptography;

namespace wavio.Utilities.Crypto;

/// <summary>
/// AES-256-GCM envelope cipher (see <see cref="IEnvelopeCipher"/> for the rationale,
/// including why callers should pass row/tenant-binding AAD).
///
/// Wire format (inside the base64 payload) — fixed-size fields first so the only
/// variable-length field, the data ciphertext, is simply "everything remaining":
///   [12-byte key-nonce][16-byte key-tag][32-byte wrapped data key]
///   [12-byte data-nonce][16-byte data-tag][data ciphertext bytes]
///
/// The caller-supplied AAD is folded into AES-GCM's associatedData for BOTH the
/// key-wrap and data layers (not stored in the payload — the caller must supply
/// the same AAD again at decrypt time). It is not itself confidential, only
/// authenticated: a wrong or missing AAD makes GCM's tag check fail.
///
/// Stored value format: "envelope:v1:" + Base64(above).
/// </summary>
public sealed class AesGcmEnvelopeCipher : IEnvelopeCipher
{
    internal const string Prefix = "envelope:v1:";

    private const int NonceSize = 12;   // AES-GCM recommended nonce size
    private const int TagSize = 16;     // 128-bit authentication tag
    private const int KeySizeBytes = 32; // AES-256, for both master and data keys
    private const int HeaderSize = NonceSize + TagSize + KeySizeBytes + NonceSize + TagSize;

    private static readonly byte[] EmptyAad = [];

    private readonly byte[] _masterKey;

    public AesGcmEnvelopeCipher(byte[] masterKey)
    {
        ArgumentNullException.ThrowIfNull(masterKey);
        RequireKeySize(masterKey, nameof(masterKey));
        _masterKey = masterKey;
    }

    /// <inheritdoc />
    public string Encrypt(byte[] plaintext, byte[]? aad)
    {
        ArgumentNullException.ThrowIfNull(plaintext);
        var aadBytes = aad ?? EmptyAad;

        var dataKey = RandomNumberGenerator.GetBytes(KeySizeBytes);
        try
        {
            var (dataNonce, dataTag, dataCiphertext) = AesGcmEncrypt(dataKey, plaintext, aadBytes);
            var (keyNonce, keyTag, wrappedKey) = AesGcmEncrypt(_masterKey, dataKey, aadBytes);

            var payload = new byte[HeaderSize + dataCiphertext.Length];
            var offset = 0;
            WriteAt(payload, ref offset, keyNonce);
            WriteAt(payload, ref offset, keyTag);
            WriteAt(payload, ref offset, wrappedKey);
            WriteAt(payload, ref offset, dataNonce);
            WriteAt(payload, ref offset, dataTag);
            WriteAt(payload, ref offset, dataCiphertext);

            return Prefix + Convert.ToBase64String(payload);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(dataKey);
        }
    }

    /// <inheritdoc />
    public byte[] Decrypt(string envelope, byte[]? aad)
    {
        var aadBytes = aad ?? EmptyAad;
        var fields = ParseEnvelope(envelope);

        var dataKey = AesGcmDecrypt(_masterKey, fields.KeyNonce, fields.KeyTag, fields.WrappedKey, aadBytes);
        try
        {
            return AesGcmDecrypt(dataKey, fields.DataNonce, fields.DataTag, fields.DataCiphertext, aadBytes);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(dataKey);
        }
    }

    /// <inheritdoc />
    public string Rewrap(string envelope, byte[] newMasterKey, byte[]? aad)
    {
        ArgumentNullException.ThrowIfNull(newMasterKey);
        RequireKeySize(newMasterKey, nameof(newMasterKey));
        var aadBytes = aad ?? EmptyAad;

        var fields = ParseEnvelope(envelope);

        var dataKey = AesGcmDecrypt(_masterKey, fields.KeyNonce, fields.KeyTag, fields.WrappedKey, aadBytes);
        try
        {
            var (keyNonce, keyTag, wrappedKey) = AesGcmEncrypt(newMasterKey, dataKey, aadBytes);

            var payload = new byte[HeaderSize + fields.DataCiphertext.Length];
            var offset = 0;
            WriteAt(payload, ref offset, keyNonce);
            WriteAt(payload, ref offset, keyTag);
            WriteAt(payload, ref offset, wrappedKey);
            WriteAt(payload, ref offset, fields.DataNonce);
            WriteAt(payload, ref offset, fields.DataTag);
            WriteAt(payload, ref offset, fields.DataCiphertext);

            return Prefix + Convert.ToBase64String(payload);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(dataKey);
        }
    }

    private static void RequireKeySize(byte[] key, string paramName)
    {
        if (key.Length != KeySizeBytes)
            throw new ArgumentException(
                $"Envelope key must be exactly {KeySizeBytes} bytes (256 bits) after decoding. Got {key.Length} bytes.",
                paramName);
    }

    private static (byte[] Nonce, byte[] Tag, byte[] Ciphertext) AesGcmEncrypt(byte[] key, byte[] plaintext, byte[] aad)
    {
        var nonce = RandomNumberGenerator.GetBytes(NonceSize);
        var tag = new byte[TagSize];
        var ciphertext = new byte[plaintext.Length];

        using var aes = new AesGcm(key, TagSize);
        aes.Encrypt(nonce, plaintext, ciphertext, tag, aad);

        return (nonce, tag, ciphertext);
    }

    private static byte[] AesGcmDecrypt(byte[] key, byte[] nonce, byte[] tag, byte[] ciphertext, byte[] aad)
    {
        var plaintext = new byte[ciphertext.Length];
        using var aes = new AesGcm(key, TagSize);
        aes.Decrypt(nonce, ciphertext, tag, plaintext, aad);
        return plaintext;
    }

    private static void WriteAt(byte[] destination, ref int offset, byte[] source)
    {
        source.CopyTo(destination, offset);
        offset += source.Length;
    }

    private readonly record struct EnvelopeFields(
        byte[] KeyNonce, byte[] KeyTag, byte[] WrappedKey,
        byte[] DataNonce, byte[] DataTag, byte[] DataCiphertext);

    private static EnvelopeFields ParseEnvelope(string envelope)
    {
        ArgumentNullException.ThrowIfNull(envelope);

        if (!envelope.StartsWith(Prefix, StringComparison.Ordinal))
            throw new CryptographicException($"Envelope value is missing the \"{Prefix}\" prefix — not a valid envelope.");

        var payload = Convert.FromBase64String(envelope[Prefix.Length..]);
        if (payload.Length < HeaderSize)
            throw new CryptographicException("Envelope payload is too short — data may be corrupted.");

        var offset = 0;
        var keyNonce = ReadAt(payload, ref offset, NonceSize);
        var keyTag = ReadAt(payload, ref offset, TagSize);
        var wrappedKey = ReadAt(payload, ref offset, KeySizeBytes);
        var dataNonce = ReadAt(payload, ref offset, NonceSize);
        var dataTag = ReadAt(payload, ref offset, TagSize);
        var dataCiphertext = payload[offset..];

        return new EnvelopeFields(keyNonce, keyTag, wrappedKey, dataNonce, dataTag, dataCiphertext);
    }

    private static byte[] ReadAt(byte[] source, ref int offset, int length)
    {
        var slice = source[offset..(offset + length)];
        offset += length;
        return slice;
    }
}
