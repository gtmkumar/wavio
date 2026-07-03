namespace wavio.Utilities.Auth;

/// <summary>
/// Claims for a system user (staff/admin) JWT.
/// token_use is always "user".
/// </summary>
public sealed record TokenClaims(
    Guid UserId,
    string UserType,
    string? Email,
    string? Phone,
    // Active scope (from X-Scope header or primary membership)
    string? ScopeType,
    Guid? ScopeId,
    Guid? TenantId,
    // Space-separated permission codes
    string Permissions,
    // Snapshot of the user's perm_version at issuance (for live revocation). Default 0.
    int PermVersion = 0,
    // Space-separated "type:id" scope nodes the user holds (platform → "platform").
    // Enables the per-request ancestor-or-self boundary check (ICurrentUser.IsWithinScope).
    string? ScopeNodes = null,
    // Space-separated high/critical permission codes the caller must step up for.
    // Always emitted for system users; read by the authz handlers to decide if step-up applies.
    string? StepUpPerms = null,
    // Authentication method reference of a fresh step-up (e.g. "otp"); emitted ONLY on a token
    // re-issued by /auth/step-up/verify — null on login/refresh.
    string? Amr = null,
    // Unix seconds of the successful step-up verify; emitted ONLY on the upgraded token. The proof
    // is fresh while now − StepUpAt ≤ StepUp.FreshnessWindow.
    long? StepUpAt = null
)
{
    /// <summary>Fixed token_use value for system users.</summary>
    public const string TokenUseValue = "user";

    /// <summary>JWT claim name carrying <see cref="PermVersion"/>.</summary>
    public const string PermVersionClaim = "perm_ver";

    /// <summary>JWT claim name carrying the space-separated high/critical codes (<see cref="StepUpPerms"/>).</summary>
    public const string StepUpPermsClaim = "step_up_perms";

    /// <summary>JWT claim name carrying the step-up auth-method reference (<see cref="Amr"/>).</summary>
    public const string AmrClaim = "amr";

    /// <summary>JWT claim name carrying the step-up timestamp in unix seconds (<see cref="StepUpAt"/>).</summary>
    public const string StepUpAtClaim = "stepup_at";
}
