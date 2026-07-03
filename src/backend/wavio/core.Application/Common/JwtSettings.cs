namespace core.Application.Common;

public sealed class JwtSettings
{
    public const string SectionName = "Jwt";

    public string Issuer { get; set; } = null!;
    public string Audience { get; set; } = null!;


    public int AccessMinutes { get; set; } = 15;
    public int RefreshDays { get; set; } = 30;

    // ── RS256 / JWKS ──────────────────────────────────────────────────────────
    /// <summary>Verifying services: base URL of the Identity issuer whose JWKS endpoint
    /// publishes the RS256 public key(s).</summary>
    public string? Authority { get; set; }

    /// <summary>Identity only: RSA private signing key as PEM (production: env / Key Vault).</summary>
    public string? PrivateKey { get; set; }

    /// <summary>Identity only: path to the RSA private key PEM (Development auto-generates it).</summary>
    public string? PrivateKeyPath { get; set; }
}
