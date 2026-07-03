using core.Application.Common.Interfaces;
using core.Application.Identity.AccessControl.Commands.GrantMembership;
using core.Application.Identity.AccessControl.Commands.RevokeMembership;
using core.Application.Identity.Users.Dtos;
using Wavio.Utilities.CQRS.Abstractions;
using Microsoft.EntityFrameworkCore;

namespace core.Application.Identity.Users.Commands.ChangePrimaryRole;

public sealed record ChangePrimaryRoleCommand(Guid UserId, ChangeRoleRequest Request, Guid? ActorId) : ICommand<MembershipDto>;

/// <summary>
/// Fix a wrongly-assigned role: grant the new role as the user's PRIMARY membership, then
/// revoke the previous primary one ("replace" semantics — the person ends with one primary).
/// Privilege checks (rank / brand-scope / platform-admin) are enforced by GrantMembership and
/// run BEFORE anything is revoked, so an unauthorized attempt changes nothing.
/// </summary>
public class ChangePrimaryRoleCommandHandler : ICommandHandler<ChangePrimaryRoleCommand, MembershipDto>
{
    private readonly ICoreDbContext _db;
    private readonly IDispatcher _dispatcher;
    public ChangePrimaryRoleCommandHandler(ICoreDbContext db, IDispatcher dispatcher) { _db = db; _dispatcher = dispatcher; }

    public async Task<MembershipDto> HandleAsync(ChangePrimaryRoleCommand cmd, CancellationToken ct)
    {
        // No one may change their OWN primary role — a self-lockout / self-escalation
        // guard enforced server-side so it holds regardless of the UI (the drawer also
        // hides the control). Applies to everyone, platform admins included.
        if (cmd.ActorId.HasValue && cmd.ActorId.Value == cmd.UserId)
            throw new UnauthorizedAccessException("You cannot change your own role.");

        // Snapshot the current primary membership(s) before changing anything.
        var oldPrimaryIds = await _db.UserScopeMemberships
            .Where(m => m.UserId == cmd.UserId && m.IsPrimary && m.RevokedAt == null)
            .Select(m => m.Id)
            .ToListAsync(ct);

        // Grant the new role as primary (reuses every guard; throws before we revoke if denied).
        var dto = await _dispatcher.SendAsync(new GrantMembershipCommand(
            new GrantMembershipRequest(cmd.UserId, cmd.Request.ScopeType, cmd.Request.ScopeId, cmd.Request.RoleId, IsPrimary: true),
            cmd.ActorId), ct);

        // Replace: revoke the old primary membership(s), skipping the one we just created.
        foreach (var id in oldPrimaryIds.Where(id => id != dto.Id))
            await _dispatcher.SendAsync(new RevokeMembershipCommand(
                new RevokeMembershipRequest(id, "Replaced via change-role"), cmd.ActorId), ct);

        return dto;
    }
}
