using System.Security.Cryptography;
using System.Text;
using wavio.Utilities.Crypto;
using Xunit;

namespace wavio.Utilities.Tests.Crypto;

public class AesGcmEnvelopeCipherTests
{
    private static byte[] NewKey() => RandomNumberGenerator.GetBytes(32);
    private static byte[] Aad(string s) => Encoding.UTF8.GetBytes(s);

    [Fact]
    public void Encrypt_then_Decrypt_round_trips_the_original_plaintext()
    {
        var masterKey = NewKey();
        var cipher = new AesGcmEnvelopeCipher(masterKey);
        var plaintext = Encoding.UTF8.GetBytes("EAAG-example-meta-system-user-token");

        var envelope = cipher.Encrypt(plaintext, aad: null);
        var decrypted = cipher.Decrypt(envelope, aad: null);

        Assert.Equal(plaintext, decrypted);
    }

    [Fact]
    public void Encrypt_output_carries_the_envelope_v1_prefix_and_is_not_the_plaintext()
    {
        var cipher = new AesGcmEnvelopeCipher(NewKey());
        var plaintext = Encoding.UTF8.GetBytes("secret-value");

        var envelope = cipher.Encrypt(plaintext, aad: null);

        Assert.StartsWith("envelope:v1:", envelope, StringComparison.Ordinal);
        Assert.DoesNotContain("secret-value", envelope, StringComparison.Ordinal);
    }

    [Fact]
    public void Encrypt_produces_a_different_envelope_each_time_for_the_same_plaintext()
    {
        // Random data key + random nonces per call — this is what makes each encrypted
        // row look unrelated to every other, even for repeated/predictable plaintexts.
        var cipher = new AesGcmEnvelopeCipher(NewKey());
        var plaintext = Encoding.UTF8.GetBytes("same-value");

        var envelopeA = cipher.Encrypt(plaintext, aad: null);
        var envelopeB = cipher.Encrypt(plaintext, aad: null);

        Assert.NotEqual(envelopeA, envelopeB);
        Assert.Equal(plaintext, cipher.Decrypt(envelopeA, aad: null));
        Assert.Equal(plaintext, cipher.Decrypt(envelopeB, aad: null));
    }

    [Fact]
    public void Decrypt_with_the_wrong_master_key_fails()
    {
        var cipher = new AesGcmEnvelopeCipher(NewKey());
        var envelope = cipher.Encrypt(Encoding.UTF8.GetBytes("secret-value"), aad: null);

        var wrongKeyCipher = new AesGcmEnvelopeCipher(NewKey());

        // AesGcm throws AuthenticationTagMismatchException specifically (a
        // CryptographicException subtype) when the GCM tag doesn't verify.
        Assert.Throws<AuthenticationTagMismatchException>(() => wrongKeyCipher.Decrypt(envelope, aad: null));
    }

    [Fact]
    public void Tampering_with_the_ciphertext_is_detected_by_the_GCM_tag()
    {
        var cipher = new AesGcmEnvelopeCipher(NewKey());
        var envelope = cipher.Encrypt(Encoding.UTF8.GetBytes("secret-value"), aad: null);

        // Flip a byte deep enough to land in the data-ciphertext region, not just the
        // wrapped-key/nonce header — either should be caught, this targets ciphertext.
        var payloadBytes = Convert.FromBase64String(envelope["envelope:v1:".Length..]);
        payloadBytes[^1] ^= 0xFF;
        var tampered = "envelope:v1:" + Convert.ToBase64String(payloadBytes);

        Assert.Throws<AuthenticationTagMismatchException>(() => cipher.Decrypt(tampered, aad: null));
    }

    [Fact]
    public void Rewrap_lets_the_new_master_key_decrypt_the_same_plaintext()
    {
        var oldMasterKey = NewKey();
        var newMasterKey = NewKey();
        var plaintext = Encoding.UTF8.GetBytes("secret-value");

        var oldCipher = new AesGcmEnvelopeCipher(oldMasterKey);
        var envelope = oldCipher.Encrypt(plaintext, aad: null);

        var rewrapped = oldCipher.Rewrap(envelope, newMasterKey, aad: null);

        var newCipher = new AesGcmEnvelopeCipher(newMasterKey);
        Assert.Equal(plaintext, newCipher.Decrypt(rewrapped, aad: null));
    }

    [Fact]
    public void Rewrap_invalidates_the_old_master_key()
    {
        var oldMasterKey = NewKey();
        var newMasterKey = NewKey();
        var oldCipher = new AesGcmEnvelopeCipher(oldMasterKey);
        var envelope = oldCipher.Encrypt(Encoding.UTF8.GetBytes("secret-value"), aad: null);

        var rewrapped = oldCipher.Rewrap(envelope, newMasterKey, aad: null);

        // The rewrapped envelope's data key is now only wrapped under newMasterKey —
        // the old cipher (still holding oldMasterKey) must no longer be able to open it.
        Assert.Throws<AuthenticationTagMismatchException>(() => oldCipher.Decrypt(rewrapped, aad: null));
    }

    [Fact]
    public void Rewrap_does_not_change_the_encrypted_payload_only_the_wrapped_key()
    {
        // The whole point of envelope encryption: rotating the master key is O(1) per
        // record (re-wrap ~32 bytes), never touches/re-encrypts the actual payload.
        var oldMasterKey = NewKey();
        var newMasterKey = NewKey();
        var oldCipher = new AesGcmEnvelopeCipher(oldMasterKey);
        var envelope = oldCipher.Encrypt(Encoding.UTF8.GetBytes("secret-value"), aad: null);

        var rewrapped = oldCipher.Rewrap(envelope, newMasterKey, aad: null);

        var originalPayload = Convert.FromBase64String(envelope["envelope:v1:".Length..]);
        var rewrappedPayload = Convert.FromBase64String(rewrapped["envelope:v1:".Length..]);

        // Header is [12 key-nonce][16 key-tag][32 wrapped-key][12 data-nonce][16 data-tag] = 88 bytes.
        var dataCiphertextOffset = 12 + 16 + 32 + 12 + 16;
        var originalDataCiphertext = originalPayload[dataCiphertextOffset..];
        var rewrappedDataCiphertext = rewrappedPayload[dataCiphertextOffset..];

        Assert.Equal(originalDataCiphertext, rewrappedDataCiphertext);
    }

    [Fact]
    public void Rewrap_with_the_same_AAD_still_decrypts_under_the_new_key()
    {
        var oldMasterKey = NewKey();
        var newMasterKey = NewKey();
        var plaintext = Encoding.UTF8.GetBytes("secret-value");
        var aad = Aad("tenant-a:waba.business_accounts:row-1");

        var oldCipher = new AesGcmEnvelopeCipher(oldMasterKey);
        var envelope = oldCipher.Encrypt(plaintext, aad);

        var rewrapped = oldCipher.Rewrap(envelope, newMasterKey, aad);

        var newCipher = new AesGcmEnvelopeCipher(newMasterKey);
        Assert.Equal(plaintext, newCipher.Decrypt(rewrapped, aad));
    }

    [Theory]
    [InlineData(16)]
    [InlineData(31)]
    [InlineData(33)]
    [InlineData(64)]
    public void Constructor_rejects_a_master_key_that_is_not_exactly_32_bytes(int keyLength)
    {
        var badKey = new byte[keyLength];

        Assert.Throws<ArgumentException>(() => new AesGcmEnvelopeCipher(badKey));
    }

    [Fact]
    public void Decrypt_rejects_a_value_missing_the_envelope_prefix()
    {
        var cipher = new AesGcmEnvelopeCipher(NewKey());

        Assert.Throws<CryptographicException>(() => cipher.Decrypt("not-an-envelope", aad: null));
    }

    [Fact]
    public void Decrypt_rejects_a_truncated_envelope()
    {
        var cipher = new AesGcmEnvelopeCipher(NewKey());
        var tooShort = "envelope:v1:" + Convert.ToBase64String(new byte[10]);

        Assert.Throws<CryptographicException>(() => cipher.Decrypt(tooShort, aad: null));
    }

    // ─── AAD (S7 security fix): row/tenant binding ──────────────────────────

    [Fact]
    public void Decrypt_with_matching_AAD_succeeds()
    {
        var cipher = new AesGcmEnvelopeCipher(NewKey());
        var plaintext = Encoding.UTF8.GetBytes("EAAG-meta-token-for-tenant-a");
        var aad = Aad("tenant-a:waba.business_accounts:row-1");

        var envelope = cipher.Encrypt(plaintext, aad);

        Assert.Equal(plaintext, cipher.Decrypt(envelope, aad));
    }

    [Fact]
    public void Decrypt_with_a_different_AAD_than_was_used_to_encrypt_fails()
    {
        var cipher = new AesGcmEnvelopeCipher(NewKey());
        var envelope = cipher.Encrypt(
            Encoding.UTF8.GetBytes("EAAG-meta-token-for-tenant-a"),
            Aad("tenant-a:waba.business_accounts:row-1"));

        Assert.Throws<AuthenticationTagMismatchException>(
            () => cipher.Decrypt(envelope, Aad("tenant-b:waba.business_accounts:row-1")));
    }

    [Fact]
    public void Decrypt_with_no_AAD_fails_when_encrypted_with_AAD()
    {
        var cipher = new AesGcmEnvelopeCipher(NewKey());
        var envelope = cipher.Encrypt(
            Encoding.UTF8.GetBytes("secret-value"),
            Aad("tenant-a:waba.business_accounts:row-1"));

        Assert.Throws<AuthenticationTagMismatchException>(() => cipher.Decrypt(envelope, aad: null));
    }

    [Fact]
    public void Swapping_two_rows_ciphertext_is_detected_when_AAD_is_the_row_identity()
    {
        // The exact scenario the AAD parameter exists to catch: two tenants' rows,
        // same master key, ciphertext accidentally (or maliciously) swapped between
        // them via a bad UPDATE. Without AAD, each envelope is self-consistent (its
        // own random data key) and would decrypt "successfully" into the wrong
        // tenant's plaintext with no error. With AAD = row identity, the swap is
        // caught because the ciphertext for row A was never authenticated for row B.
        var cipher = new AesGcmEnvelopeCipher(NewKey());

        var rowAAad = Aad("tenant-a:waba.business_accounts:row-1");
        var rowBAad = Aad("tenant-b:waba.business_accounts:row-2");

        var rowAEnvelope = cipher.Encrypt(Encoding.UTF8.GetBytes("tenant-a-real-token"), rowAAad);
        var rowBEnvelope = cipher.Encrypt(Encoding.UTF8.GetBytes("tenant-b-real-token"), rowBAad);

        // Simulate the swap: row B's DB column now (erroneously) holds row A's envelope.
        var swappedIntoRowB = rowAEnvelope;

        // The app always decrypts using the AAD it independently knows belongs to
        // row B (its own tenant/row id) — it does not trust anything from the
        // envelope itself to supply that context.
        Assert.Throws<AuthenticationTagMismatchException>(() => cipher.Decrypt(swappedIntoRowB, rowBAad));

        // Sanity: the swapped envelope is still perfectly valid for the row it
        // actually belongs to — this isn't corruption, it's a misplaced-but-intact value.
        Assert.Equal(Encoding.UTF8.GetBytes("tenant-a-real-token"), cipher.Decrypt(swappedIntoRowB, rowAAad));
    }
}
