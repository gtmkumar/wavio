namespace wavio.Utilities.Auth;

/// <summary>
/// Bridge for permission-code renames: maps a legacy permission code to its current name so
/// authorization keeps working across a rename while already-issued JWTs (whose <c>permissions</c>
/// claim is baked at login) still carry the old codes.
///
/// <para><b>Active bridge — multi-vertical Phase 1 (blueprint Risk #6):</b> <c>garment.*</c> →
/// <c>fulfillment.*</c>. A token minted before the rename holds <c>garment.read</c>; endpoints now
/// require <c>fulfillment.read</c>. <see cref="Canonical"/> normalizes BOTH the held claims and the
/// required code to the current name before comparison, so old and new tokens both pass. Remove these
/// entries once all tokens have cycled and the Phase-2 per-vertical seeder split has landed.</para>
/// </summary>
public static class PermissionAlias
{
    private static readonly IReadOnlyDictionary<string, string> LegacyToCurrent =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["garment.read"]    = "fulfillment.read",
            ["garment.tag"]     = "fulfillment.tag",
            ["garment.inspect"] = "fulfillment.inspect",
        };

    /// <summary>The current canonical form of a permission code (identity if not aliased).</summary>
    public static string Canonical(string code)
        => LegacyToCurrent.TryGetValue(code, out var current) ? current : code;
}
