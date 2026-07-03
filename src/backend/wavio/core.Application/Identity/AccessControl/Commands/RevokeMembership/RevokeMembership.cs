using core.Application.Common.Interfaces;
using core.Application.Identity.Users.Dtos;
using wavio.SharedDataModel.Enums;
using wavio.Utilities.Auth.Audit;
using wavio.Utilities.Exceptions;
using wavio.Utilities.Services;
using Wavio.Utilities.CQRS.Abstractions;

namespace core.Application.Identity.AccessControl.Commands.RevokeMembership;

public sealed record RevokeMembershipCommand(RevokeMembershipRequest Request, Guid? ActorId) : ICommand<bool>;

public class RevokeMembershipCommandHandler : ICommandHandler<RevokeMembershipCommand, bool>
{
    private readonly ICoreDbContext _db;
    private readonly ICurrentUser _user;
    private readonly IAuditWriter _audit;
    public RevokeMembershipCommandHandler(ICoreDbContext db, ICurrentUser user, IAuditWriter audit)
    {
        _db = db;
        _user = user;
        _audit = audit;
    }

    public async Task<bool> HandleAsync(RevokeMembershipCommand cmd, CancellationToken ct)
    {
        var m = await _db.UserScopeMemberships.FindAsync([cmd.Request.MembershipId], ct);
        if (m is null || m.RevokedAt.HasValue) return false;

        // Scope guard: the membership is loaded by id (bypassing query filters), so the caller
        // must sit at-or-above the membership's scope node before revoking it. Scope is
        // polymorphic (ScopeType + ScopeId); a platform-scoped membership (ScopeId null) may
        // only be revoked by a platform admin.
        var targetTenantId = m.ScopeType == ScopeType.Tenant ? m.ScopeId : null;
        if (m.ScopeType == ScopeType.Platform
            ? !_user.IsPlatformAdmin
            : !_user.IsWithinScope(targetTenantId))
        {
            throw new ForbiddenException("This membership is outside your assigned scope.");
        }

        m.RevokedAt = DateTimeOffset.UtcNow; m.RevokedBy = cmd.ActorId; m.RevokedReason = cmd.Request.Reason;
        await _db.SaveChangesAsync(ct);

        // Invalidate the user's existing tokens (live revocation).
        await core.Application.Identity.Common.PermVersionBumper.BumpUserAsync(_db, m.UserId, ct);

        // Semantic audit: privilege revocation — which membership (user/scope/role) was pulled.
        await _audit.WriteAsync("membership.revoke", "user_scope_memberships", m.Id,
            resourceDisplay: $"Revoked membership @ {m.ScopeType}",
            oldValues: new { m.UserId, m.ScopeType, m.ScopeId, m.RoleId, Reason = cmd.Request.Reason },
            ct: ct);
        return true;
    }
}
