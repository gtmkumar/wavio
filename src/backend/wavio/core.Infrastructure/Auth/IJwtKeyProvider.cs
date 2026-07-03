using Microsoft.IdentityModel.Tokens;

namespace core.Infrastructure.Auth;

/// <summary>
/// Supplies the RSA key material used to sign (and locally validate) JWTs, and the
/// public JWK published at the JWKS endpoint. Identity is the only token issuer; all
/// other services verify RS256 signatures by fetching the public key from JWKS.
/// </summary>
public interface IJwtKeyProvider
{
    /// <summary>RSA private signing key (carries <c>KeyId</c> = kid). Used for RS256 signing
    /// and Identity's own local token validation.</summary>
    RsaSecurityKey SigningKey { get; }

    /// <summary>Public-only JWK for the JWKS document (never contains private params).</summary>
    JsonWebKey PublicJwk { get; }

    /// <summary>Stable key id, surfaced in the JWT header and the JWK.</summary>
    string KeyId { get; }
}
