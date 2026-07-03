namespace wavio.Utilities.Crypto;

/// <summary>
/// Envelope encryption: each plaintext is encrypted under its own random data
/// key, which is itself wrapped by a single master key. Rotating the master
/// key (<see cref="Rewrap"/>) only re-wraps the small data key, not the
/// (potentially large) payload.
///
/// This is the app-level replacement for the spec's KMS references (issue
/// #12, docs/BUILD_PLAN.md "Secrets (no cloud KMS)") — the master key itself
/// comes from an env var or a 0600 file on the VPS, never checked in.
///
/// Minimal surface only: no consumers yet. Meta system-user token storage
/// (waba.business_accounts.system_user_token_ciphertext) wires this up in
/// Wave 1.
/// </summary>
public interface IEnvelopeCipher
{
    /// <summary>Encrypts <paramref name="plaintext"/> into a self-contained envelope string.</summary>
    string Encrypt(byte[] plaintext);

    /// <summary>Decrypts an envelope produced by <see cref="Encrypt"/>.</summary>
    byte[] Decrypt(string envelope);

    /// <summary>
    /// Re-wraps an envelope's data key under <paramref name="newMasterKey"/>, without
    /// touching the encrypted payload. Use during master-key rotation: decrypt nothing,
    /// re-encrypt nothing except the ~32-byte data key.
    /// </summary>
    string Rewrap(string envelope, byte[] newMasterKey);
}
