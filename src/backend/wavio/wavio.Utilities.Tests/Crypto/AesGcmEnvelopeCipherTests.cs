using System.Security.Cryptography;
using System.Text;
using wavio.Utilities.Crypto;
using Xunit;

namespace wavio.Utilities.Tests.Crypto;

public class AesGcmEnvelopeCipherTests
{
    private static byte[] NewKey() => RandomNumberGenerator.GetBytes(32);

    [Fact]
    public void Encrypt_then_Decrypt_round_trips_the_original_plaintext()
    {
        var masterKey = NewKey();
        var cipher = new AesGcmEnvelopeCipher(masterKey);
        var plaintext = Encoding.UTF8.GetBytes("EAAG-example-meta-system-user-token");

        var envelope = cipher.Encrypt(plaintext);
        var decrypted = cipher.Decrypt(envelope);

        Assert.Equal(plaintext, decrypted);
    }

    [Fact]
    public void Encrypt_output_carries_the_envelope_v1_prefix_and_is_not_the_plaintext()
    {
        var cipher = new AesGcmEnvelopeCipher(NewKey());
        var plaintext = Encoding.UTF8.GetBytes("secret-value");

        var envelope = cipher.Encrypt(plaintext);

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

        var envelopeA = cipher.Encrypt(plaintext);
        var envelopeB = cipher.Encrypt(plaintext);

        Assert.NotEqual(envelopeA, envelopeB);
        Assert.Equal(plaintext, cipher.Decrypt(envelopeA));
        Assert.Equal(plaintext, cipher.Decrypt(envelopeB));
    }

    [Fact]
    public void Decrypt_with_the_wrong_master_key_fails()
    {
        var cipher = new AesGcmEnvelopeCipher(NewKey());
        var envelope = cipher.Encrypt(Encoding.UTF8.GetBytes("secret-value"));

        var wrongKeyCipher = new AesGcmEnvelopeCipher(NewKey());

        // AesGcm throws AuthenticationTagMismatchException specifically (a
        // CryptographicException subtype) when the GCM tag doesn't verify.
        Assert.Throws<AuthenticationTagMismatchException>(() => wrongKeyCipher.Decrypt(envelope));
    }

    [Fact]
    public void Tampering_with_the_ciphertext_is_detected_by_the_GCM_tag()
    {
        var cipher = new AesGcmEnvelopeCipher(NewKey());
        var envelope = cipher.Encrypt(Encoding.UTF8.GetBytes("secret-value"));

        // Flip a byte deep enough to land in the data-ciphertext region, not just the
        // wrapped-key/nonce header — either should be caught, this targets ciphertext.
        var payloadBytes = Convert.FromBase64String(envelope["envelope:v1:".Length..]);
        payloadBytes[^1] ^= 0xFF;
        var tampered = "envelope:v1:" + Convert.ToBase64String(payloadBytes);

        Assert.Throws<AuthenticationTagMismatchException>(() => cipher.Decrypt(tampered));
    }

    [Fact]
    public void Rewrap_lets_the_new_master_key_decrypt_the_same_plaintext()
    {
        var oldMasterKey = NewKey();
        var newMasterKey = NewKey();
        var plaintext = Encoding.UTF8.GetBytes("secret-value");

        var oldCipher = new AesGcmEnvelopeCipher(oldMasterKey);
        var envelope = oldCipher.Encrypt(plaintext);

        var rewrapped = oldCipher.Rewrap(envelope, newMasterKey);

        var newCipher = new AesGcmEnvelopeCipher(newMasterKey);
        Assert.Equal(plaintext, newCipher.Decrypt(rewrapped));
    }

    [Fact]
    public void Rewrap_invalidates_the_old_master_key()
    {
        var oldMasterKey = NewKey();
        var newMasterKey = NewKey();
        var oldCipher = new AesGcmEnvelopeCipher(oldMasterKey);
        var envelope = oldCipher.Encrypt(Encoding.UTF8.GetBytes("secret-value"));

        var rewrapped = oldCipher.Rewrap(envelope, newMasterKey);

        // The rewrapped envelope's data key is now only wrapped under newMasterKey —
        // the old cipher (still holding oldMasterKey) must no longer be able to open it.
        Assert.Throws<AuthenticationTagMismatchException>(() => oldCipher.Decrypt(rewrapped));
    }

    [Fact]
    public void Rewrap_does_not_change_the_encrypted_payload_only_the_wrapped_key()
    {
        // The whole point of envelope encryption: rotating the master key is O(1) per
        // record (re-wrap ~32 bytes), never touches/re-encrypts the actual payload.
        var oldMasterKey = NewKey();
        var newMasterKey = NewKey();
        var oldCipher = new AesGcmEnvelopeCipher(oldMasterKey);
        var envelope = oldCipher.Encrypt(Encoding.UTF8.GetBytes("secret-value"));

        var rewrapped = oldCipher.Rewrap(envelope, newMasterKey);

        var originalPayload = Convert.FromBase64String(envelope["envelope:v1:".Length..]);
        var rewrappedPayload = Convert.FromBase64String(rewrapped["envelope:v1:".Length..]);

        // Header is [12 key-nonce][16 key-tag][32 wrapped-key][12 data-nonce][16 data-tag] = 88 bytes.
        var dataCiphertextOffset = 12 + 16 + 32 + 12 + 16;
        var originalDataCiphertext = originalPayload[dataCiphertextOffset..];
        var rewrappedDataCiphertext = rewrappedPayload[dataCiphertextOffset..];

        Assert.Equal(originalDataCiphertext, rewrappedDataCiphertext);
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

        Assert.Throws<CryptographicException>(() => cipher.Decrypt("not-an-envelope"));
    }

    [Fact]
    public void Decrypt_rejects_a_truncated_envelope()
    {
        var cipher = new AesGcmEnvelopeCipher(NewKey());
        var tooShort = "envelope:v1:" + Convert.ToBase64String(new byte[10]);

        Assert.Throws<CryptographicException>(() => cipher.Decrypt(tooShort));
    }
}
