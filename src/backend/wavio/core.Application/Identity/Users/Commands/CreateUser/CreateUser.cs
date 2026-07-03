using System.Security.Cryptography;
using core.Application.Common.Interfaces;
using core.Application.Identity.Users.Dtos;
using Wavio.Utilities.CQRS.Abstractions;
using wavio.SharedDataModel.Entities.IdentityAccess;
using wavio.SharedDataModel.Enums;
using wavio.Utilities.Auth.Audit;
using wavio.Utilities.Exceptions;

namespace core.Application.Identity.Users.Commands.CreateUser;

public sealed record CreateUserCommand(CreateUserRequest Request, Guid? ActorId) : ICommand<UserDto>;

public class CreateUserCommandHandler : ICommandHandler<CreateUserCommand, UserDto>
{
    private readonly ICoreDbContext _db;
    private readonly IPasswordHasher _hasher;
    private readonly IAuditWriter _audit;
    public CreateUserCommandHandler(ICoreDbContext db, IPasswordHasher hasher, IAuditWriter audit)
    { _db = db; _hasher = hasher; _audit = audit; }

    public async Task<UserDto> HandleAsync(CreateUserCommand cmd, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(cmd.Request.Email) && string.IsNullOrEmpty(cmd.Request.Phone))
            throw new ValidationException(
                new Dictionary<string, string[]> { ["identifier"] = ["Either email or phone is required."] });

        // The user_type is client-supplied (incl. via the invite flow). Reject anything outside
        // the known set so a misconfigured/hostile caller can't persist an arbitrary type that
        // the DB CHECK, auth scope resolution, and vertical routing would later choke on.
        if (!UserType.IsValid(cmd.Request.UserType))
            throw new ValidationException(
                new Dictionary<string, string[]> { ["userType"] = [$"'{cmd.Request.UserType}' is not a valid user type."] });

        var inviteToken = cmd.Request.Password is null
            ? Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
            : null;

        var user = new User
        {
            Id              = Guid.NewGuid(),
            Email           = string.IsNullOrEmpty(cmd.Request.Email)   ? null : cmd.Request.Email,
            PhoneE164       = string.IsNullOrEmpty(cmd.Request.Phone)   ? null : cmd.Request.Phone,
            PasswordHash    = cmd.Request.Password is not null ? _hasher.Hash(cmd.Request.Password) : null,
            UserType        = cmd.Request.UserType,
            Status          = cmd.Request.Password is null ? UserStatus.Invited : UserStatus.Active,
            InvitationToken = inviteToken,
            InvitationSentAt = inviteToken is not null ? DateTimeOffset.UtcNow : null,
            Locale          = "en-IN",
            Timezone        = "Asia/Kolkata",
            FailedAttempts  = 0,
            CreatedAt       = DateTimeOffset.UtcNow,
            UpdatedAt       = DateTimeOffset.UtcNow,
            CreatedBy       = cmd.ActorId,
            Version         = 1
        };
        _db.Users.Add(user);

        if (cmd.Request.FirstName is not null || cmd.Request.LastName is not null || cmd.Request.Designation is not null)
        {
            _db.UserProfiles.Add(new UserProfile
            {
                UserId      = user.Id,
                FirstName   = cmd.Request.FirstName,
                LastName    = cmd.Request.LastName,
                Designation = cmd.Request.Designation,
                Preferences = "{}", Metadata = "{}", Status = "active",
                CreatedAt   = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow, CreatedBy = cmd.ActorId
            });
        }

        await _db.SaveChangesAsync(ct);

        // Semantic audit: account creation (also covers the invite flow, which delegates here).
        // Identifiers only — never the password/hash or invitation token.
        await _audit.WriteAsync("user.create", "users", user.Id,
            resourceDisplay: user.Email ?? user.PhoneE164,
            newValues: new { user.Email, user.PhoneE164, user.UserType, user.Status }, ct: ct);

        return new UserDto(user.Id, user.Email, user.PhoneE164, user.UserType, user.Status,
            user.MfaEnabled, user.LastLoginAt, user.CreatedAt,
            cmd.Request.FirstName, cmd.Request.LastName, null);
    }
}
