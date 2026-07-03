using System.Security.Cryptography;
using core.Application.Common;
using Microsoft.Extensions.Hosting;
using Microsoft.IdentityModel.Tokens;

namespace core.Infrastructure.Auth;

/// <summary>
/// Loads the RSA signing key for RS256 JWTs.
///
/// Resolution order:
///   1. Jwt:PrivateKey      — inline PEM (production: injected from env / Key Vault).
///   2. Jwt:PrivateKeyPath  — path to a PEM file.
///   3. Development only     — generate a 2048-bit key and persist it to PrivateKeyPath
///      (default keys/dev-jwt-signing.pem, git-ignored) so it is stable across restarts.
///
/// Outside Development a key MUST be provided — the constructor throws otherwise
/// (fail closed; never auto-generate a signing key in production).
/// </summary>
public sealed class RsaJwtKeyProvider : IJwtKeyProvider
{
    public RsaSecurityKey SigningKey { get; }
    public JsonWebKey     PublicJwk  { get; }
    public string         KeyId      { get; }

    public RsaJwtKeyProvider(JwtSettings settings, IHostEnvironment env)
    {
        var rsa = RSA.Create();

        if (!string.IsNullOrWhiteSpace(settings.PrivateKey))
        {
            rsa.ImportFromPem(settings.PrivateKey);
        }
        else if (!string.IsNullOrWhiteSpace(settings.PrivateKeyPath) && File.Exists(settings.PrivateKeyPath))
        {
            rsa.ImportFromPem(File.ReadAllText(settings.PrivateKeyPath));
        }
        else if (env.IsDevelopment())
        {
            // Generate once and persist so every restart (and every service's JWKS cache)
            // sees a stable key in local development.
            rsa = RSA.Create(2048);
            var path = string.IsNullOrWhiteSpace(settings.PrivateKeyPath)
                ? Path.Combine(AppContext.BaseDirectory, "keys", "dev-jwt-signing.pem")
                : settings.PrivateKeyPath;
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, rsa.ExportPkcs8PrivateKeyPem());
        }
        else
        {
            throw new InvalidOperationException(
                "Jwt:PrivateKey (or Jwt:PrivateKeyPath) must be provided outside Development. " +
                "Identity will not auto-generate a signing key in production.");
        }

        // Stable kid = base64url(SHA-256(public modulus)). Deterministic for a given key.
        var pub = rsa.ExportParameters(false);
        KeyId = Base64UrlEncoder.Encode(SHA256.HashData(pub.Modulus!));

        SigningKey = new RsaSecurityKey(rsa) { KeyId = KeyId };

        // Public-only JWK for the JWKS document.
        PublicJwk = JsonWebKeyConverter.ConvertFromRSASecurityKey(
            new RsaSecurityKey(pub) { KeyId = KeyId });
        PublicJwk.Use = "sig";
        PublicJwk.Alg = SecurityAlgorithms.RsaSha256;
    }
}
