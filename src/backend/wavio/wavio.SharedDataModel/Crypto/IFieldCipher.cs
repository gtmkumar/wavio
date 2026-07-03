namespace wavio.SharedDataModel.Crypto;

/// <summary>
/// Application-layer field cipher for PII at rest.
/// Encrypts with AES-256-GCM; ciphertext is prefixed with "enc:v1:" so converters
/// can distinguish encrypted values from legacy plaintext rows and pass those through.
/// </summary>
public interface IFieldCipher
{
    /// <summary>
    /// Encrypts <paramref name="plaintext"/> and returns a prefixed base64 ciphertext string.
    /// Null input returns null.
    /// </summary>
    string? Encrypt(string? plaintext);

    /// <summary>
    /// Decrypts a previously encrypted value, or returns the original string if it is
    /// legacy plaintext (does not carry the "enc:v1:" prefix).
    /// Null input returns null.
    /// </summary>
    string? Decrypt(string? value);
}
