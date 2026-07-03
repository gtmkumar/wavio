using wavio.SharedDataModel.Entities.IdentityAccess;

namespace core.Application.Common.Interfaces;

/// <summary>
/// Handles the self-referential family_id FK constraint on refresh_tokens.
/// The root token in a family has family_id = its own id (a circular reference EF cannot
/// insert in one step), so the root insert uses raw parameterized SQL. Rotated tokens use
/// EF normally (their family_id already points to an existing row).
/// </summary>
public interface IRefreshTokenRepository
{
    /// <summary>
    /// Inserts a brand-new root refresh token (family_id = token.Id) via raw parameterized SQL.
    /// </summary>
    Task InsertRootAsync(RefreshToken token, CancellationToken ct);
}
