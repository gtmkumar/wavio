using core.Application.Common.Interfaces;
using core.Application.Identity.Users.Dtos;
using Wavio.Utilities.CQRS.Abstractions;
using wavio.Utilities.Services;

namespace core.Application.Identity.Users.Commands.SetUserType;

public sealed record SetUserTypeCommand(Guid UserId, SetUserTypeRequest Request) : ICommand<bool>;

/// <summary>
/// H3: Changes a user's type. Only callable by actors whose own type is at the same
/// level or higher than the requested type (prevent self-elevation).
/// Requires permission users.set_type.
/// </summary>
public class SetUserTypeCommandHandler : ICommandHandler<SetUserTypeCommand, bool>
{
    // Priority: lower value = higher privilege. Mirrors the seeded roles.Priority ordering.
    private static readonly Dictionary<string, int> TypePriority = new(StringComparer.OrdinalIgnoreCase)
    {
        [wavio.SharedDataModel.Enums.UserType.PlatformAdmin]     = 10,
        [wavio.SharedDataModel.Enums.UserType.TenantAdmin]       = 20,
        [wavio.SharedDataModel.Enums.UserType.Staff]             = 60,
        [wavio.SharedDataModel.Enums.UserType.Auditor]           = 100,
        [wavio.SharedDataModel.Enums.UserType.Support]           = 110,
    };

    private readonly ICoreDbContext _db;
    private readonly ICurrentUser _actor;
    public SetUserTypeCommandHandler(ICoreDbContext db, ICurrentUser actor) { _db = db; _actor = actor; }

    public async Task<bool> HandleAsync(SetUserTypeCommand cmd, CancellationToken ct)
    {
        var actor = _actor;

        // Only platform_admin can assign platform_admin type
        if (cmd.Request.NewUserType == wavio.SharedDataModel.Enums.UserType.PlatformAdmin
            && actor.UserType != wavio.SharedDataModel.Enums.UserType.PlatformAdmin)
        {
            throw new UnauthorizedAccessException(
                "Only a platform_admin may assign the platform_admin user type.");
        }

        // Actor's own type must have equal or higher privilege than the type being assigned
        var actorPriority  = TypePriority.GetValueOrDefault(actor.UserType ?? string.Empty, int.MaxValue);
        var targetPriority = TypePriority.GetValueOrDefault(cmd.Request.NewUserType, int.MaxValue);

        if (targetPriority < actorPriority)
        {
            throw new UnauthorizedAccessException(
                "You cannot assign a user type with higher privileges than your own.");
        }

        var user = await _db.Users.FindAsync([cmd.UserId], ct);
        if (user is null) return false;

        user.UserType  = cmd.Request.NewUserType;
        user.UpdatedAt = DateTimeOffset.UtcNow;
        user.UpdatedBy = actor.UserId;
        user.Version++;

        await _db.SaveChangesAsync(ct);
        return true;
    }
}
