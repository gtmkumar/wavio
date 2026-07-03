using wavio.SharedDataModel.Enums;

namespace wavio.Utilities.Auth;

/// <summary>
/// One node in the scope hierarchy a user holds a membership at
/// (platform ⊃ brand ⊃ territory ⊃ franchise ⊃ store|warehouse — see docs/rbac.md §3).
/// Carried in the JWT <c>scope_nodes</c> claim so per-request handlers can enforce the
/// §6 ancestor-or-self boundary without a DB round-trip: a permission only applies when the
/// target resource's scope node is at or below one of the user's membership nodes.
/// </summary>
/// <param name="ScopeType">One of <see cref="ScopeType"/> (platform/brand/territory/franchise/store/warehouse).</param>
/// <param name="ScopeId">The specific node id; <c>null</c> for platform (which has no id).</param>
public readonly record struct ScopeNode(string ScopeType, Guid? ScopeId)
{
    /// <summary>Wire form used in the <c>scope_nodes</c> claim: <c>"type:id"</c>, or just <c>"platform"</c>.</summary>
    public string Encode() => ScopeId is { } id ? $"{ScopeType}:{id}" : ScopeType;

    /// <summary>Parse one <c>"type:id"</c> token; returns null for malformed input.</summary>
    public static ScopeNode? TryParse(string token)
    {
        if (string.IsNullOrWhiteSpace(token)) return null;
        var i = token.IndexOf(':');
        if (i < 0) return new ScopeNode(token, null); // e.g. "platform"
        var type = token[..i];
        return Guid.TryParse(token[(i + 1)..], out var id)
            ? new ScopeNode(type, id)
            : null;
    }
}
