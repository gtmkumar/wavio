using System.Security.Claims;

namespace wavio.Utilities.Auth;

/// <summary>
/// Step-up (fresh re-verification) helpers for high/critical actions — docs/rbac.md §8.
/// Pure reads over the already-validated principal: no DB, no injected state, so the singleton
/// permission handlers stay dependency-free and behave identically across all three hosts
/// (core validates with a static key, operations/commerce via JWKS — both yield the same claims).
/// </summary>
public static class StepUp
{
    /// <summary>How recently the caller must have re-verified for a high/critical action to pass.
    /// Matches OtpSettings.TtlMinutes (5) and the §8 example window; independent of the access-token exp.</summary>
    public static readonly TimeSpan FreshnessWindow = TimeSpan.FromMinutes(5);

    /// <summary>True when <paramref name="requiredCanonicalCode"/> is one the caller must step up for —
    /// i.e. it is present in the signed <c>step_up_perms</c> claim (the caller's high/critical codes,
    /// or the full high/critical catalog for platform_admin). Pass an already-canonicalized code.</summary>
    public static bool RequiresStepUp(ClaimsPrincipal user, string requiredCanonicalCode)
    {
        var claim = user.FindFirstValue(TokenClaims.StepUpPermsClaim);
        if (string.IsNullOrEmpty(claim)) return false;
        foreach (var code in claim.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            if (string.Equals(PermissionAlias.Canonical(code), requiredCanonicalCode, StringComparison.OrdinalIgnoreCase))
                return true;
        return false;
    }

    /// <summary>True when the caller holds a step-up proof fresher than <see cref="FreshnessWindow"/> —
    /// the <c>stepup_at</c> unix-seconds claim stamped by POST /auth/step-up/verify.</summary>
    public static bool IsFresh(ClaimsPrincipal user) => IsFresh(user, FreshnessWindow);

    public static bool IsFresh(ClaimsPrincipal user, TimeSpan window)
    {
        var raw = user.FindFirstValue(TokenClaims.StepUpAtClaim);
        if (!long.TryParse(raw, out var unixSeconds)) return false;
        var stampedAt = DateTimeOffset.FromUnixTimeSeconds(unixSeconds);
        var age = DateTimeOffset.UtcNow - stampedAt;
        return age >= TimeSpan.Zero && age <= window;
    }
}
