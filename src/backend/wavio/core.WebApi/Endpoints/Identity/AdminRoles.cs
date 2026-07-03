using core.Application.Identity.AccessControl.Commands.AssignPermission;
using core.Application.Identity.AccessControl.Commands.GrantMembership;
using core.Application.Identity.AccessControl.Commands.RevokeMembership;
using core.Application.Identity.AccessControl.Queries.GetPermissions;
using core.Application.Identity.AccessControl.Queries.GetRoles;
using core.Application.Identity.Users.Dtos;
using Wavio.Utilities.CQRS.Abstractions;
using wavio.Utilities.ApiResponse.ResponseUtil;
using wavio.Utilities.Endpoints;
using wavio.Utilities.Services;

namespace core.WebApi.Endpoints.Identity;

/// <summary>
/// Admin — Roles &amp; permissions (/api/v1/admin/roles). Membership grants run the
/// privilege-escalation guards (rank / brand-scope / platform-admin) inside the handler.
/// </summary>
public class AdminRoles : IEndpointGroup
{
    public static string? RoutePrefix => "/api/v1/admin/roles";

    public static void Map(RouteGroupBuilder group)
    {
        group.WithTags("Admin - Roles").RequireAuthorization();

        group.MapGet(GetRoles).RequireAuthorization("permission:roles.list");
        group.MapGet(GetPermissions, "permissions").RequireAuthorization("permission:permissions.list");
        group.MapPost(AssignPermission, "assign-permission").RequireAuthorization("permission:permissions.assign");
        group.MapPost(GrantMembership, "memberships/grant").RequireAuthorization("permission:memberships.grant");
        group.MapPost(RevokeMembership, "memberships/revoke").RequireAuthorization("permission:memberships.revoke");
    }

    public static async Task<IResult> GetRoles(IDispatcher dispatcher, CancellationToken ct, int page = 1, int pageSize = 50)
    {
        var data = await dispatcher.QueryAsync(new GetRolesQuery(page < 1 ? 1 : page, pageSize < 1 ? 50 : pageSize), ct);
        return Results.Ok(new ListResponse<RoleDto> { Status = true, Data = data });
    }

    public static async Task<IResult> GetPermissions(string? module, IDispatcher dispatcher, CancellationToken ct)
    {
        var data = await dispatcher.QueryAsync(new GetPermissionsQuery(module), ct);
        return Results.Ok(new ListResponse<PermissionDto> { Status = true, Data = data });
    }

    public static async Task<IResult> AssignPermission(AssignPermissionRequest req, ICurrentUser user, IDispatcher dispatcher, CancellationToken ct)
    {
        await dispatcher.SendAsync(new AssignPermissionCommand(req, user.UserId), ct);
        return Results.Ok(new Response { Status = true });
    }

    public static async Task<IResult> GrantMembership(GrantMembershipRequest req, ICurrentUser user, IDispatcher dispatcher, CancellationToken ct)
    {
        var data = await dispatcher.SendAsync(new GrantMembershipCommand(req, user.UserId), ct);
        return Results.Ok(new SingleResponse<MembershipDto> { Status = true, Data = data });
    }

    public static async Task<IResult> RevokeMembership(RevokeMembershipRequest req, ICurrentUser user, IDispatcher dispatcher, CancellationToken ct)
    {
        var ok = await dispatcher.SendAsync(new RevokeMembershipCommand(req, user.UserId), ct);
        return ok ? Results.Ok(new Response { Status = true }) : Results.NotFound();
    }
}
