using core.Application.Common.Interfaces;
using core.Application.Identity.Users.Dtos;
using Wavio.Utilities.CQRS.Abstractions;
using wavio.SharedDataModel.Entities.IdentityAccess;
using Microsoft.EntityFrameworkCore;

namespace core.Application.Identity.AccessControl.Commands.AssignPermission;

public sealed record AssignPermissionCommand(AssignPermissionRequest Request, Guid? ActorId) : ICommand<bool>;

public class AssignPermissionCommandHandler : ICommandHandler<AssignPermissionCommand, bool>
{
    private readonly ICoreDbContext _db;
    public AssignPermissionCommandHandler(ICoreDbContext db) => _db = db;

    public async Task<bool> HandleAsync(AssignPermissionCommand cmd, CancellationToken ct)
    {
        if (await _db.RolePermissions.AnyAsync(rp => rp.RoleId == cmd.Request.RoleId && rp.PermissionId == cmd.Request.PermissionId, ct))
            return true; // idempotent
        _db.RolePermissions.Add(new RolePermission
        {
            Id = Guid.NewGuid(), RoleId = cmd.Request.RoleId, PermissionId = cmd.Request.PermissionId,
            GrantedAt = DateTimeOffset.UtcNow, GrantedBy = cmd.ActorId, CreatedAt = DateTimeOffset.UtcNow, CreatedBy = cmd.ActorId
        });
        await _db.SaveChangesAsync(ct);
        return true;
    }
}
