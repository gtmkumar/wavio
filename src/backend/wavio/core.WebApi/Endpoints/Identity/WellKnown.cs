using core.Application.Common;
using core.Infrastructure.Auth;
using wavio.Utilities.Endpoints;
using Microsoft.Extensions.Options;

namespace core.WebApi.Endpoints.Identity;

/// <summary>
/// OIDC-style discovery + JWKS endpoints. Identity is the sole JWT issuer; every other
/// service verifies RS256 signatures by fetching the public key published here. Anonymous.
/// Routes live at the root (<c>/.well-known/*</c>), so the group prefix is empty.
/// </summary>
public class WellKnown : IEndpointGroup
{
    public static string? RoutePrefix => "";

    public static void Map(RouteGroupBuilder group)
    {
        group.WithTags("Well-Known").AllowAnonymous();

        group.MapGet(Jwks, "/.well-known/jwks.json");
        group.MapGet(OpenIdConfiguration, "/.well-known/openid-configuration");
    }

    // GET /.well-known/jwks.json — the RS256 public key set.
    public static IResult Jwks(IJwtKeyProvider keys)
    {
        var jwk = keys.PublicJwk;
        return Results.Json(new
        {
            keys = new[]
            {
                new
                {
                    kty = jwk.Kty,
                    use = jwk.Use,
                    kid = jwk.Kid,
                    alg = jwk.Alg,
                    n   = jwk.N,
                    e   = jwk.E,
                }
            }
        });
    }

    // GET /.well-known/openid-configuration — minimal discovery doc so verifying services'
    // JwtBearer ConfigurationManager can locate the JWKS.
    public static IResult OpenIdConfiguration(HttpContext ctx, IOptions<JwtSettings> jwt)
    {
        var baseUrl = $"{ctx.Request.Scheme}://{ctx.Request.Host}";
        return Results.Json(new
        {
            issuer   = jwt.Value.Issuer,
            jwks_uri = $"{baseUrl}/.well-known/jwks.json",
            id_token_signing_alg_values_supported = new[] { "RS256" },
            response_types_supported = new[] { "token" },
            subject_types_supported  = new[] { "public" },
        });
    }
}
