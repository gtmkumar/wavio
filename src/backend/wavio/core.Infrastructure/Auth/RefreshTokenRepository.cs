using core.Application.Common.Interfaces;
using wavio.SharedDataModel.Entities.IdentityAccess;
using wavio.SharedDataModel.Persistence;
using Microsoft.EntityFrameworkCore;

namespace core.Infrastructure.Auth;

/// <summary>
/// Handles the self-referential family_id FK constraint on refresh_tokens.
/// The root token in a family has family_id = its own id (circular reference EF can't insert in one step).
/// We use raw parameterized SQL for the root insert; rotated tokens use EF normally (their family_id
/// already points to an existing row in the same table).
///
/// Injects the concrete <see cref="WavioDbContext"/> (allowed in Infrastructure) because
/// the raw INSERT goes through <c>Database.ExecuteSqlAsync</c>, which is not on the
/// <c>ICoreDbContext</c> surface.
/// </summary>
public sealed class RefreshTokenRepository : IRefreshTokenRepository
{
    private readonly WavioDbContext _db;

    public RefreshTokenRepository(WavioDbContext db) => _db = db;

    /// <summary>
    /// Inserts a brand-new root refresh token (family_id = token.Id).
    /// Uses raw SQL because EF cannot resolve the self-referential FK in a single INSERT.
    /// </summary>
    public async Task InsertRootAsync(RefreshToken token, CancellationToken ct)
    {
        // Ensure family_id = token.Id for root tokens
        token.FamilyId = token.Id;

        await _db.Database.ExecuteSqlAsync(
            $"""
            INSERT INTO identity_access.refresh_tokens
              (id, user_id, token_hash, family_id, parent_token_id,
               device_id, device_name, device_os, ip_address, user_agent,
               issued_at, expires_at, last_used_at, revoked_at, revoked_reason, created_at)
            VALUES
              ({token.Id}, {token.UserId}, {token.TokenHash}, {token.Id}, {token.ParentTokenId},
               {token.DeviceId}, {token.DeviceName}, {token.DeviceOs}, {token.IpAddress}, {token.UserAgent},
               {token.IssuedAt}, {token.ExpiresAt}, {token.LastUsedAt}, {token.RevokedAt}, {token.RevokedReason}, {token.CreatedAt})
            """,
            ct);
    }
}
