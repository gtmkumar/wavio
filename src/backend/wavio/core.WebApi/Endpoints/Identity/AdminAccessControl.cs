using core.Application.Identity.AccessControl.Commands.InviteUser;
using core.Application.Identity.AccessControl.Commands.ManageRoles;
using core.Application.Identity.AccessControl.Commands.SetPersonStatus;
using core.Application.Identity.AccessControl.Commands.SetRoleCells;
using core.Application.Identity.AccessControl.Commands.SetUserPermissionOverride;
using core.Application.Identity.AccessControl.Dtos;
using core.Application.Identity.AccessControl.Queries.GetAccessPeople;
using core.Application.Identity.AccessControl.Queries.GetAccessRoles;
using core.Application.Identity.Users.Dtos;
using Wavio.Utilities.CQRS.Abstractions;
using wavio.Utilities.ApiResponse.ResponseUtil;
using wavio.Utilities.Endpoints;
using wavio.Utilities.Services;

namespace core.WebApi.Endpoints.Identity;

/// <summary>
/// Admin — Access Control console (/api/v1/admin/access-control): People / Roles &amp; Permissions
/// tabs plus invite + status writes. Privilege-escalation guards live in the handlers.
/// </summary>
public class AdminAccessControl : IEndpointGroup
{
    public static string? RoutePrefix => "/api/v1/admin/access-control";

    public static void Map(RouteGroupBuilder group)
    {
        group.WithTags("Admin - Access Control").RequireAuthorization();

        group.MapGet(GetAccessPeople, "people").RequireAuthorization("permission:users.list");
        group.MapGet(GetAccessRoles, "roles").RequireAuthorization("permission:roles.list");
        group.MapPost(InviteUser, "invite").RequireAuthorization("permission:users.create");
        group.MapPost(SetRoleCells, "roles/{id:guid}/cells").RequireAuthorization("permission:permissions.assign");
        // Role CRUD (UI-managed custom roles). Gated by roles.manage.
        group.MapPost(CreateRole, "roles").RequireAuthorization("permission:roles.manage");
        group.MapPut(UpdateRole, "roles/{id:guid}").RequireAuthorization("permission:roles.manage");
        group.MapDelete(DeleteRole, "roles/{id:guid}").RequireAuthorization("permission:roles.manage");
        group.MapPost(CloneRole, "roles/{id:guid}/clone").RequireAuthorization("permission:roles.manage");
        group.MapPost(SetUserPermissionOverride, "people/{id:guid}/permission-override").RequireAuthorization("permission:permissions.assign");
        group.MapPost(SetPersonStatus, "people/{id:guid}/status").RequireAuthorization("permission:users.update");
    }

    public static async Task<IResult> GetAccessPeople(string? search, string? sort,
        IDispatcher dispatcher, CancellationToken ct, int page = 1, int pageSize = 100)
    {
        var data = await dispatcher.QueryAsync(
            new GetAccessPeopleQuery(search, page < 1 ? 1 : page, pageSize < 1 ? 100 : pageSize, sort), ct);
        return Results.Ok(new SingleResponse<AccessPeoplePageDto> { Status = true, Data = data });
    }

    public static async Task<IResult> GetAccessRoles(IDispatcher dispatcher, CancellationToken ct)
    {
        var data = await dispatcher.QueryAsync(new GetAccessRolesQuery(), ct);
        return Results.Ok(new SingleResponse<AccessRolesDto> { Status = true, Data = data });
    }

    public static async Task<IResult> InviteUser(InviteUserRequest req, IDispatcher dispatcher, CancellationToken ct)
    {
        var data = await dispatcher.SendAsync(new InviteUserCommand(req), ct);
        return Results.Ok(new SingleResponse<UserDto> { Status = true, Data = data });
    }

    public static async Task<IResult> SetRoleCells(Guid id, SetRoleCellsRequest req, ICurrentUser user, IDispatcher dispatcher, CancellationToken ct)
    {
        var ok = await dispatcher.SendAsync(new SetRoleCellsCommand(id, req, user.UserId), ct);
        return ok ? Results.Ok(new Response { Status = true }) : Results.NotFound();
    }

    public static async Task<IResult> CreateRole(CreateRoleRequest req, ICurrentUser user, IDispatcher dispatcher, CancellationToken ct)
    {
        var data = await dispatcher.SendAsync(new CreateRoleCommand(req, user.UserId), ct);
        return Results.Ok(new SingleResponse<RoleSummaryDto> { Status = true, Data = data });
    }

    public static async Task<IResult> UpdateRole(Guid id, UpdateRoleRequest req, ICurrentUser user, IDispatcher dispatcher, CancellationToken ct)
    {
        var ok = await dispatcher.SendAsync(new UpdateRoleCommand(id, req, user.UserId), ct);
        return ok ? Results.Ok(new Response { Status = true }) : Results.NotFound();
    }

    public static async Task<IResult> DeleteRole(Guid id, ICurrentUser user, IDispatcher dispatcher, CancellationToken ct)
    {
        var ok = await dispatcher.SendAsync(new DeleteRoleCommand(id, user.UserId), ct);
        return ok ? Results.Ok(new Response { Status = true }) : Results.NotFound();
    }

    public static async Task<IResult> CloneRole(Guid id, CloneRoleRequest req, ICurrentUser user, IDispatcher dispatcher, CancellationToken ct)
    {
        var data = await dispatcher.SendAsync(new CloneRoleCommand(id, req, user.UserId), ct);
        return Results.Ok(new SingleResponse<RoleSummaryDto> { Status = true, Data = data });
    }

    public static async Task<IResult> SetUserPermissionOverride(Guid id, SetUserPermissionOverrideRequest req,
        ICurrentUser user, IDispatcher dispatcher, CancellationToken ct)
    {
        var ok = await dispatcher.SendAsync(new SetUserPermissionOverrideCommand(id, req, user.UserId), ct);
        return ok ? Results.Ok(new Response { Status = true }) : Results.NotFound();
    }

    public static async Task<IResult> SetPersonStatus(Guid id, SetPersonStatusRequest req, ICurrentUser user, IDispatcher dispatcher, CancellationToken ct)
    {
        var data = await dispatcher.SendAsync(new SetPersonStatusCommand(id, req, user.UserId), ct);
        return data is null
            ? Results.NotFound()
            : Results.Ok(new SingleResponse<SetPersonStatusResult> { Status = true, Data = data });
    }
}
