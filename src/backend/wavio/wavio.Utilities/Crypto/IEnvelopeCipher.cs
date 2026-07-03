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
/// <para><b>AAD (additional authenticated data)</b> — pass the row/tenant
/// context a ciphertext belongs to (e.g. a stable string like
/// <c>"{tenantId}:waba.business_accounts:{id}"</c>, UTF-8 encoded). AAD is
/// authenticated but not encrypted: it binds a ciphertext to the row it was
/// created for, so decrypting with a different (or missing) AAD than the one
/// used to encrypt fails loudly. Without this, swapping two ciphertext values
/// between rows — e.g. two tenants' encrypted WABA tokens swapped by a bad
/// UPDATE or malicious DB access — decrypts "successfully" into the wrong
/// row's value with no error, since each envelope is otherwise
/// self-consistent (its own random data key). Pass the same identifier used
/// at encrypt time; pass <c>null</c> only when there is genuinely no
/// row/tenant context to bind to.</para>
///
/// Minimal surface only: no consumers yet. Meta system-user token storage
/// (waba.business_accounts.system_user_token_ciphertext) wires this up in
/// Wave 1.
/// </summary>
public interface IEnvelopeCipher
{
    /// <summary>Encrypts <paramref name="plaintext"/> into a self-contained envelope string.</summary>
    string Encrypt(byte[] plaintext, byte[]? aad);

    /// <summary>
    /// Decrypts an envelope produced by <see cref="Encrypt"/>. <paramref name="aad"/> must
    /// match what was passed to <see cref="Encrypt"/> exactly, or decryption fails.
    /// </summary>
    byte[] Decrypt(string envelope, byte[]? aad);

    /// <summary>
    /// Re-wraps an envelope's data key under <paramref name="newMasterKey"/>, without
    /// touching the encrypted payload. Use during master-key rotation: decrypt nothing,
    /// re-encrypt nothing except the ~32-byte data key. <paramref name="aad"/> must match
    /// what the envelope was created with — the row/tenant binding doesn't change on rotation.
    /// </summary>
    string Rewrap(string envelope, byte[] newMasterKey, byte[]? aad);
}
