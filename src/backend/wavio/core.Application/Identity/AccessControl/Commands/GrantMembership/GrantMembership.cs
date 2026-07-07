using core.Application.Common.Interfaces;
using core.Application.Identity.Users.Dtos;
using Wavio.Utilities.CQRS.Abstractions;
using wavio.SharedDataModel.Entities.IdentityAccess;
using wavio.SharedDataModel.Enums;
using wavio.Utilities.Auth.Audit;
using wavio.Utilities.Exceptions;
using wavio.Utilities.Services;
using Microsoft.EntityFrameworkCore;

namespace core.Application.Identity.AccessControl.Commands.GrantMembership;

/// <summary>ActorId carries the calling user's identity for privilege-escalation checks.</summary>
public sealed record GrantMembershipCommand(GrantMembershipRequest Request, Guid? ActorId) : ICommand<MembershipDto>;

public class GrantMembershipCommandHandler : ICommandHandler<GrantMembershipCommand, MembershipDto>
{
    private readonly ICoreDbContext _db;
    private readonly ICurrentUser _actor;
    private readonly IAuditWriter _audit;
    public GrantMembershipCommandHandler(ICoreDbContext db, ICurrentUser actor, IAuditWriter audit)
    { _db = db; _actor = actor; _audit = audit; }

    public async Task<MembershipDto> HandleAsync(GrantMembershipCommand cmd, CancellationToken ct)
    {
        var actor = _actor;

        // ── granting platform_admin role requires the actor to BE platform_admin ──
        var targetRole = await _db.Roles.FindAsync([cmd.Request.RoleId], ct)
            ?? throw new ValidationException(
                new Dictionary<string, string[]> { ["roleId"] = ["Role not found."] });

        if (targetRole.Code == "platform_admin" &&
            actor.UserType != UserType.PlatformAdmin)
        {
            throw new UnauthorizedAccessException(
                "Only a platform_admin may grant the platform_admin role.");
        }

        // ── Defense-in-depth: a tenant-scoped role MUST bind to a concrete tenant ──
        // The UI sends it, but if it's missing fall back to the actor's own tenant and
        // reject if neither is available. Persisting a tenant membership with a null
        // scope issues a token with no tenant_id, which locks the user out of every
        // tenant-scoped service with a 401.
        var effectiveScopeId = cmd.Request.ScopeId;
        if (cmd.Request.ScopeType == ScopeType.Tenant && effectiveScopeId is null)
        {
            // TryGetTenantId, not the raw claim: platform admins have no tenant_id
            // claim — their acting tenant arrives via the X-Tenant-Id override.
            effectiveScopeId = actor.TryGetTenantId()
                ?? throw new ValidationException(
                    new Dictionary<string, string[]> { ["scopeId"] = ["Tenant-scoped roles require a tenant id."] });
        }

        // ── Scope guard: the target scope node must lie within the actor's assigned scope ──
        // Platform admins pass automatically; a tenant-scoped actor may only grant a membership
        // at their own tenant.
        var targetTenantId = cmd.Request.ScopeType == ScopeType.Tenant ? effectiveScopeId : null;
        if (!_actor.IsWithinScope(targetTenantId))
        {
            throw new ForbiddenException("This membership is outside your assigned scope.");
        }

        // ── actor's role priority must be <= role being granted (lower number = higher rank) ──
        // Fetch actor's highest-privilege role (lowest priority number)
        if (!actor.IsPlatformAdmin)
        {
            var actorMinPriority = await _db.UserScopeMemberships
                .AsNoTracking()
                .Where(m => m.UserId == cmd.ActorId
                         && m.RevokedAt == null
                         && (m.ExpiresAt == null || m.ExpiresAt > DateTimeOffset.UtcNow))
                .Join(_db.Roles.IgnoreQueryFilters(),
                      m => m.RoleId,
                      r => r.Id,
                      (m, r) => r.Priority)
                .MinAsync(ct);   // lower priority number = higher rank

            if (targetRole.Priority < actorMinPriority)
            {
                throw new UnauthorizedAccessException(
                    "You cannot grant a role with higher privileges than your own.");
            }
        }

        // ── Apply primary flag ─────────────────────────────────────────────────
        if (cmd.Request.IsPrimary)
        {
            var existingPrimary = await _db.UserScopeMemberships
                .Where(m => m.UserId == cmd.Request.UserId && m.IsPrimary && m.RevokedAt == null)
                .ToListAsync(ct);
            existingPrimary.ForEach(m => m.IsPrimary = false);
        }

        var membership = new UserScopeMembership
        {
            Id        = Guid.NewGuid(),
            UserId    = cmd.Request.UserId,
            ScopeType = cmd.Request.ScopeType,
            ScopeId   = effectiveScopeId,
            RoleId    = cmd.Request.RoleId,
            IsPrimary = cmd.Request.IsPrimary,
            GrantedAt = DateTimeOffset.UtcNow,
            GrantedBy = cmd.ActorId,
            Metadata  = "{}",
            CreatedAt = DateTimeOffset.UtcNow,
            CreatedBy = cmd.ActorId
        };
        _db.UserScopeMemberships.Add(membership);
        await _db.SaveChangesAsync(ct);

        // Invalidate the target user's existing tokens (live revocation).
        await Common.PermVersionBumper.BumpUserAsync(_db, cmd.Request.UserId, ct);

        // Semantic audit: privilege grant — who got which role at which scope.
        await _audit.WriteAsync("membership.grant", "user_scope_memberships", membership.Id,
            resourceDisplay: $"{targetRole.Code} @ {membership.ScopeType}",
            newValues: new
            {
                membership.UserId,
                membership.ScopeType,
                membership.ScopeId,
                RoleCode = targetRole.Code,
                membership.IsPrimary
            },
            ct: ct);

        return new MembershipDto(
            membership.Id, membership.UserId, membership.ScopeType, membership.ScopeId,
            membership.RoleId, targetRole.Code, membership.IsPrimary, membership.GrantedAt);
    }
}
