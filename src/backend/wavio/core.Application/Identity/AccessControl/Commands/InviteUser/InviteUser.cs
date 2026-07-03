using core.Application.Common.Interfaces;
using core.Application.Identity.AccessControl.Commands.GrantMembership;
using core.Application.Identity.AccessControl.Dtos;
using core.Application.Identity.Users.Commands.CreateUser;
using core.Application.Identity.Users.Dtos;
using Wavio.Utilities.CQRS.Abstractions;
using wavio.Utilities.Services;
using Microsoft.Extensions.Logging;

namespace core.Application.Identity.AccessControl.Commands.InviteUser;

// ── Invite user (create + grant primary membership) ─────────────────────────
public sealed record InviteUserCommand(InviteUserRequest Request) : ICommand<UserDto>;

public class InviteUserCommandHandler : ICommandHandler<InviteUserCommand, UserDto>
{
    private readonly IDispatcher _dispatcher;
    private readonly ICoreDbContext _db;
    private readonly ISettingsMailer _mailer;
    private readonly ICurrentUser _actor;
    private readonly ILogger<InviteUserCommandHandler> _log;

    public InviteUserCommandHandler(IDispatcher dispatcher, ICoreDbContext db, ISettingsMailer mailer, ICurrentUser actor, ILogger<InviteUserCommandHandler> log)
    { _dispatcher = dispatcher; _db = db; _mailer = mailer; _actor = actor; _log = log; }

    public async Task<UserDto> HandleAsync(InviteUserCommand cmd, CancellationToken ct)
    {
        var r = cmd.Request;
        var user = await _dispatcher.SendAsync(new CreateUserCommand(
            new CreateUserRequest(r.Email, r.Phone, r.UserType, r.Password, r.FirstName, r.LastName, null),
            _actor.UserId), ct);

        await _dispatcher.SendAsync(new GrantMembershipCommand(
            new GrantMembershipRequest(user.Id, r.ScopeType, r.ScopeId, r.RoleId, IsPrimary: true),
            _actor.UserId), ct);

        await InviteEmailSender.SendAsync(_db, _mailer, _log, _actor, user.Id, r.Email,
            $"{r.FirstName} {r.LastName}".Trim(), ct);
        return user;
    }
}
