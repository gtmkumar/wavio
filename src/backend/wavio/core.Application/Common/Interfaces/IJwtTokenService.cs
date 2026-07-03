using wavio.Utilities.Auth;

namespace core.Application.Common.Interfaces;

/// <summary>Issues and validates JWT access tokens.</summary>
public interface IJwtTokenService
{
    /// <summary>Creates a signed access JWT for the given system user + scope. Emits token_use=user.</summary>
    string CreateAccessToken(TokenClaims claims);

    /// <summary>Creates a raw (pre-hash) refresh token string. Caller is responsible for hashing and persisting.</summary>
    string GenerateRefreshTokenRaw();

    /// <summary>Hashes a raw refresh token string using SHA-256.</summary>
    string HashRefreshToken(string raw);
}
