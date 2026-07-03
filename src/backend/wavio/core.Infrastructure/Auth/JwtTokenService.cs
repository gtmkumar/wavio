using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using core.Application.Common;
using core.Application.Common.Interfaces;
using wavio.Utilities.Auth;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace core.Infrastructure.Auth;

/// <summary>
/// RS256-backed JWT service. The signing key is provided by <see cref="IJwtKeyProvider"/>.
/// </summary>
public sealed class JwtTokenService : IJwtTokenService
{
    private readonly JwtSettings     _settings;
    private readonly IJwtKeyProvider _keys;

    public JwtTokenService(IOptions<JwtSettings> options, IJwtKeyProvider keys)
    {
        _settings = options.Value;
        _keys     = keys;
    }

    public string CreateAccessToken(TokenClaims claims)
    {
        // RS256 — signed with the RSA private key; the kid travels in the JWT header.
        var creds = new SigningCredentials(_keys.SigningKey, SecurityAlgorithms.RsaSha256);

        var claimsList = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, claims.UserId.ToString()),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
            // Pinned contract: token_use=user distinguishes system tokens from any other lane.
            new("token_use", TokenClaims.TokenUseValue),
            new("user_type", claims.UserType),
        };

        if (!string.IsNullOrEmpty(claims.Email))
            claimsList.Add(new Claim(JwtRegisteredClaimNames.Email, claims.Email));
        if (!string.IsNullOrEmpty(claims.Phone))
            claimsList.Add(new Claim("phone", claims.Phone));
        if (claims.ScopeType is not null)
            claimsList.Add(new Claim("scope_type", claims.ScopeType));
        if (claims.ScopeId.HasValue)
            claimsList.Add(new Claim("scope_id", claims.ScopeId.Value.ToString()));
        if (claims.TenantId.HasValue)
            claimsList.Add(new Claim("tenant_id", claims.TenantId.Value.ToString()));
        if (!string.IsNullOrEmpty(claims.Permissions))
            claimsList.Add(new Claim("permissions", claims.Permissions));
        // ALWAYS emit scope_nodes for user tokens — even when empty. A membership-less principal
        // (e.g. a global user_permission_override ALLOW with no memberships) would otherwise carry
        // NO scope_nodes claim, and IsWithinScope's absent-claim fail-open (rollout safety for
        // pre-feature tokens) would let it pass EVERY scope guard. Emitting an EMPTY claim makes
        // IsWithinScope loop 0 nodes → deny. The absent-claim path now only means pre-feature tokens.
        claimsList.Add(new Claim("scope_nodes", claims.ScopeNodes ?? string.Empty));
        // Step-up: high/critical codes always travel; amr + stepup_at only on a token upgraded
        // by /auth/step-up/verify (login/refresh leave them null → freshness naturally lapses).
        if (!string.IsNullOrEmpty(claims.StepUpPerms))
            claimsList.Add(new Claim(TokenClaims.StepUpPermsClaim, claims.StepUpPerms));
        if (!string.IsNullOrEmpty(claims.Amr))
            claimsList.Add(new Claim(TokenClaims.AmrClaim, claims.Amr));
        if (claims.StepUpAt is { } stepUpAt)
            claimsList.Add(new Claim(TokenClaims.StepUpAtClaim, stepUpAt.ToString(), ClaimValueTypes.Integer64));
        claimsList.Add(new Claim(TokenClaims.PermVersionClaim, claims.PermVersion.ToString()));

        return WriteToken(claimsList, creds);
    }

    private string WriteToken(IEnumerable<Claim> claims, SigningCredentials creds)
    {
        var token = new JwtSecurityToken(
            issuer:             _settings.Issuer,
            audience:           _settings.Audience,
            claims:             claims,
            notBefore:          DateTime.UtcNow,
            expires:            DateTime.UtcNow.AddMinutes(_settings.AccessMinutes),
            signingCredentials: creds);
        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    public string GenerateRefreshTokenRaw()
    {
        return Convert.ToBase64String(RandomNumberGenerator.GetBytes(64));
    }

    public string HashRefreshToken(string raw)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(raw));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }
}
