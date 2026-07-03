namespace wavio.SharedDataModel.Contracts;

/// <summary>
/// Reads a user's current <c>perm_version</c> (via a SECURITY DEFINER function so it works
/// under normal RLS), with a short in-process cache. Used by the request pipeline to reject
/// tokens whose stamped perm_version is stale — forcing a silent refresh (live revocation).
/// Returns null when the user is unknown / unreadable, in which case callers fail OPEN.
/// </summary>
public interface ITokenVersionStore
{
    Task<int?> GetPermVersionAsync(Guid userId, CancellationToken ct);
}
