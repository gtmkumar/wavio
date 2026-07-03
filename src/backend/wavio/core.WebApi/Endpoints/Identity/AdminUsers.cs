using core.Application.Identity.AccessControl.Commands.ResendInvite;
using core.Application.Identity.Users.Commands.ChangePrimaryRole;
using core.Application.Identity.Users.Commands.CreateUser;
using core.Application.Identity.Users.Commands.DeactivateUser;
using core.Application.Identity.Users.Commands.SetPassword;
using core.Application.Identity.Users.Commands.SetUserType;
using core.Application.Identity.Users.Commands.UpdateUser;
using core.Application.Identity.Users.Dtos;
using core.Application.Identity.Users.Queries.GetUserById;
using core.Application.Identity.Users.Queries.GetUsers;
using Wavio.Utilities.CQRS.Abstractions;
using wavio.Utilities.ApiResponse.ResponseUtil;
using wavio.Utilities.Endpoints;
using wavio.Utilities.Services;

namespace core.WebApi.Endpoints.Identity;

/// <summary>
/// Admin — User management (/api/v1/admin/users). Thin: each method dispatches a command/query
/// through <see cref="IDispatcher"/>. Privileged operations (set-type, change-role) carry their
/// own permission gate and enforce privilege-escalation guards inside the handlers.
/// </summary>
public class AdminUsers : IEndpointGroup
{
    public static string? RoutePrefix => "/api/v1/admin/users";

    public static void Map(RouteGroupBuilder group)
    {
        group.WithTags("Admin - Users").RequireAuthorization();

        group.MapGet(GetUsers).RequireAuthorization("permission:users.list");
        group.MapGet(GetUserById, "{id:guid}").RequireAuthorization("permission:users.read");
        group.MapPost(CreateUser).RequireAuthorization("permission:users.create");
        group.MapPut(UpdateUser, "{id:guid}").RequireAuthorization("permission:users.update");
        group.MapPost(DeactivateUser, "{id:guid}/deactivate").RequireAuthorization("permission:users.deactivate");
        group.MapPost(SetPassword, "{id:guid}/set-password").RequireAuthorization("permission:users.set_password");
        // Re-send the invitation email to a still-pending user (rotates the token).
        group.MapPost(ResendInvite, "{id:guid}/resend-invite").RequireAuthorization("permission:users.create");
        // H3: Separate privileged endpoint for changing user_type.
        group.MapPost(SetUserType, "{id:guid}/set-type").RequireAuthorization("permission:users.set_type");
        // Replace a user's primary role; guarded the same as a membership grant.
        group.MapPost(ChangeRole, "{id:guid}/change-role").RequireAuthorization("permission:memberships.grant");
    }

    public static async Task<IResult> GetUsers(string? status, string? userType, string? search,
        IDispatcher dispatcher, CancellationToken ct, int page = 1, int pageSize = 20)
    {
        var data = await dispatcher.QueryAsync(
            new GetUsersQuery(page < 1 ? 1 : page, pageSize < 1 ? 20 : pageSize, status, userType, search), ct);
        return Results.Ok(new PaginatedListResponse<UserDto> { Status = true, Data = data });
    }

    public static async Task<IResult> GetUserById(Guid id, IDispatcher dispatcher, CancellationToken ct)
    {
        var data = await dispatcher.QueryAsync(new GetUserByIdQuery(id), ct);
        return data is null ? Results.NotFound() : Results.Ok(new SingleResponse<UserDto> { Status = true, Data = data });
    }

    public static async Task<IResult> CreateUser(CreateUserRequest req, ICurrentUser user, IDispatcher dispatcher, CancellationToken ct)
    {
        var data = await dispatcher.SendAsync(new CreateUserCommand(req, user.UserId), ct);
        return Results.Created($"/api/v1/admin/users/{data.Id}",
            new SingleResponse<UserDto> { Status = true, Data = data });
    }

    public static async Task<IResult> UpdateUser(Guid id, UpdateUserRequest req, ICurrentUser user, IDispatcher dispatcher, CancellationToken ct)
    {
        var data = await dispatcher.SendAsync(new UpdateUserCommand(id, req, user.UserId), ct);
        return data is null ? Results.NotFound() : Results.Ok(new SingleResponse<UserDto> { Status = true, Data = data });
    }

    public static async Task<IResult> DeactivateUser(Guid id, ICurrentUser user, IDispatcher dispatcher, CancellationToken ct)
    {
        var ok = await dispatcher.SendAsync(new DeactivateUserCommand(id, user.UserId), ct);
        return ok ? Results.Ok(new Response { Status = true }) : Results.NotFound();
    }

    public static async Task<IResult> SetPassword(Guid id, SetPasswordRequest req, ICurrentUser user, IDispatcher dispatcher, CancellationToken ct)
    {
        var ok = await dispatcher.SendAsync(new SetPasswordCommand(id, req, user.UserId), ct);
        return ok ? Results.Ok(new Response { Status = true }) : Results.NotFound();
    }

    public static async Task<IResult> ResendInvite(Guid id, IDispatcher dispatcher, CancellationToken ct)
    {
        var ok = await dispatcher.SendAsync(new ResendInviteCommand(id), ct);
        return ok ? Results.Ok(new Response { Status = true }) : Results.NotFound();
    }

    public static async Task<IResult> SetUserType(Guid id, SetUserTypeRequest req, IDispatcher dispatcher, CancellationToken ct)
    {
        var ok = await dispatcher.SendAsync(new SetUserTypeCommand(id, req), ct);
        return ok ? Results.Ok(new Response { Status = true }) : Results.NotFound();
    }

    public static async Task<IResult> ChangeRole(Guid id, ChangeRoleRequest req, ICurrentUser user, IDispatcher dispatcher, CancellationToken ct)
    {
        var data = await dispatcher.SendAsync(new ChangePrimaryRoleCommand(id, req, user.UserId), ct);
        return Results.Ok(new SingleResponse<MembershipDto> { Status = true, Data = data });
    }
}
