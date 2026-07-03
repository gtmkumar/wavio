using System.Security.Claims;
using wavio.Utilities.Auth;
using Microsoft.AspNetCore.Http;

namespace operations.Tests.Auth;

/// <summary>
/// No-DB, no-Docker test scaffolding for the RBAC claim-logic. Builds a signed-token-shaped
/// <see cref="ClaimsPrincipal"/> (the claim NAMES/VALUES exactly match what the JWT bearer would
/// carry) so the singleton authorization handlers and <c>HttpContextCurrentUser</c> can be driven
/// directly against it — no auth server, no middleware, no database.
/// </summary>
internal static class RbacTestSupport
{
    /// <summary>
    /// Builds a principal carrying only the claims explicitly supplied. Passing <c>null</c> (the
    /// default) OMITS the claim entirely — this is the difference between an ABSENT claim and a
    /// present-but-empty claim (pass <c>""</c> for the latter), which the scope/step-up logic treats
    /// very differently (rollout fail-open vs. deny).
    /// </summary>
    public static ClaimsPrincipal Principal(
        string? tokenUse = null,
        string? userType = null,
        string? permissions = null,
        string? scopeNodes = null,
        string? scopeType = null,
        string? stepUpPerms = null,
        string? stepUpAt = null)
    {
        var claims = new List<Claim>();

        void Add(string type, string? value)
        {
            if (value is not null) claims.Add(new Claim(type, value));
        }

        Add("token_use", tokenUse);
        Add("user_type", userType);
        Add("permissions", permissions);
        Add("scope_nodes", scopeNodes);
        Add("scope_type", scopeType);
        Add(TokenClaims.StepUpPermsClaim, stepUpPerms); // "step_up_perms"
        Add(TokenClaims.StepUpAtClaim, stepUpAt);       // "stepup_at"

        // authenticationType != null → Identity.IsAuthenticated == true (mirrors a validated bearer).
        var identity = new ClaimsIdentity(claims, authenticationType: "TestBearer");
        return new ClaimsPrincipal(identity);
    }

    /// <summary>Unix-seconds string for a <c>stepup_at</c> claim offset from now (built exactly the way
    /// the auth code reads it: <see cref="DateTimeOffset.ToUnixTimeSeconds"/>).</summary>
    public static string UnixSecondsFromNow(TimeSpan offset)
        => DateTimeOffset.UtcNow.Add(offset).ToUnixTimeSeconds().ToString();

    /// <summary>Wraps a principal in a <see cref="DefaultHttpContext"/> exposed through a stub
    /// <see cref="IHttpContextAccessor"/> — the shape <c>HttpContextCurrentUser</c> reads.</summary>
    public static IHttpContextAccessor AccessorFor(ClaimsPrincipal principal)
        => new StubHttpContextAccessor(new DefaultHttpContext { User = principal });

    private sealed class StubHttpContextAccessor : IHttpContextAccessor
    {
        public StubHttpContextAccessor(HttpContext context) => HttpContext = context;
        public HttpContext? HttpContext { get; set; }
    }
}
