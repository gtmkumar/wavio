using wavio.SharedDataModel.Crypto;

namespace WaAdmin.Tests.Fakes;

/// <summary>Reversible stand-in for the AES-GCM field cipher — tests only assert that handlers
/// pass values THROUGH the cipher (prefix present), not the cryptography itself.</summary>
public sealed class FakeFieldCipher : IFieldCipher
{
    public const string Prefix = "enc:test:";

    public string? Encrypt(string? plaintext) => plaintext is null ? null : Prefix + plaintext;

    public string? Decrypt(string? value) =>
        value is null ? null : value.StartsWith(Prefix, StringComparison.Ordinal) ? value[Prefix.Length..] : value;
}
