using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace wavio.SharedDataModel.Crypto;

/// <summary>
/// EF Core <see cref="ValueConverter{TModel,TProvider}"/> that transparently
/// encrypts on write and decrypts on read using the supplied <see cref="IFieldCipher"/>.
///
/// Apply to PII columns via <c>b.Property(e => e.PanNumber).HasConversion(PiiValueConverter.Instance)</c>
/// after calling <c>PiiValueConverter.Configure(cipher)</c> once during DI setup.
///
/// Legacy plaintext rows are passed through on reads and will be re-encrypted on the
/// next write (transparent backfill-in-place for touched rows; a full backfill script
/// is a follow-up task).
/// </summary>
public sealed class PiiValueConverter : ValueConverter<string?, string?>
{
    private PiiValueConverter(IFieldCipher cipher)
        : base(
            // model → provider (write path): encrypt
            model => cipher.Encrypt(model),
            // provider → model (read path): decrypt (or pass through legacy plaintext)
            provider => cipher.Decrypt(provider))
    { }

    /// <summary>
    /// The shared converter instance. Must be initialised exactly once by calling
    /// <see cref="Configure"/> before the first <see cref="WavioDbContext"/> is constructed.
    /// </summary>
    public static PiiValueConverter? Instance { get; private set; }

    /// <summary>
    /// Initialises <see cref="Instance"/> with the given cipher.
    /// Idempotent — repeated calls with the same cipher are a no-op; repeated calls
    /// with a different cipher throw to prevent accidental key rotation without a
    /// planned migration.
    /// </summary>
    public static void Configure(IFieldCipher cipher)
    {
        ArgumentNullException.ThrowIfNull(cipher);

        if (Instance is not null)
        {
            // Already configured — same reference is fine (e.g. startup called twice
            // in integration tests); a different instance is a misconfiguration.
            if (!ReferenceEquals(Instance._cipher, cipher))
                throw new InvalidOperationException(
                    "PiiValueConverter has already been configured with a different cipher instance. " +
                    "Ensure Configure() is called exactly once during application startup.");
            return;
        }

        Instance = new PiiValueConverter(cipher);
        Instance._cipher = cipher;
    }

    /// <summary>
    /// Returns the cipher instance used by the converter.
    /// Throws if <see cref="Configure"/> has not yet been called.
    /// Used by <see cref="wavio.SharedDataModel.DependencyInjection"/> to register
    /// <see cref="IFieldCipher"/> in the DI container so that application services can
    /// encrypt/decrypt settings secrets without duplicating cipher construction.
    /// </summary>
    public static IFieldCipher GetCipher()
    {
        if (Instance is null)
            throw new InvalidOperationException(
                "PiiValueConverter has not been configured. " +
                "Ensure AddSharedDataModel() is called before resolving IFieldCipher.");
        return Instance._cipher;
    }

    // Retained so the idempotency guard can compare references.
    private IFieldCipher _cipher = null!;
}
