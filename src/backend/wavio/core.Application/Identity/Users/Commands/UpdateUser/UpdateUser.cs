using core.Application.Common.Interfaces;
using core.Application.Identity.Users.Dtos;
using Wavio.Utilities.CQRS.Abstractions;
using wavio.SharedDataModel.Entities.IdentityAccess;
using wavio.Utilities.Services;
using Microsoft.EntityFrameworkCore;

namespace core.Application.Identity.Users.Commands.UpdateUser;

public sealed record UpdateUserCommand(Guid Id, UpdateUserRequest Request, Guid? ActorId) : ICommand<UserDto?>;

public class UpdateUserCommandHandler : ICommandHandler<UpdateUserCommand, UserDto?>
{
    private readonly ICoreDbContext _db;
    private readonly ICurrentUser _actor;
    public UpdateUserCommandHandler(ICoreDbContext db, ICurrentUser actor) { _db = db; _actor = actor; }

    public async Task<UserDto?> HandleAsync(UpdateUserCommand cmd, CancellationToken ct)
    {
        var user = await _db.Users.Include(u => u.Profile).FirstOrDefaultAsync(u => u.Id == cmd.Id, ct);
        if (user is null) return null;

        var r = cmd.Request;

        // H3: Email/Phone only — Status and UserType are NOT assignable here.
        // Status → use /deactivate. UserType → use /set-type (users.set_type permission).
        if (r.Email is not null) user.Email     = r.Email;
        if (r.Phone is not null) user.PhoneE164 = r.Phone;
        user.UpdatedAt = DateTimeOffset.UtcNow; user.UpdatedBy = cmd.ActorId; user.Version++;

        // Does the request touch any profile-backed field? Employees carry employment + KYC
        // + bank details on the profile, so a person with no profile row needs one created.
        var touchesProfile = r.FirstName is not null || r.LastName is not null || r.Designation is not null
            || r.EmploymentType is not null || r.PanNumber is not null || r.AadhaarNumberMasked is not null
            || r.KycStatus is not null || r.BankAccountName is not null || r.BankAccountNumber is not null
            || r.BankIfsc is not null || r.UpiId is not null;

        var profile = user.Profile;
        if (profile is null && touchesProfile)
        {
            profile = new UserProfile
            {
                UserId = user.Id, Preferences = "{}", Metadata = "{}", Status = "active",
                CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow, CreatedBy = cmd.ActorId,
            };
            _db.UserProfiles.Add(profile);
            user.Profile = profile;
        }

        if (profile is not null && touchesProfile)
        {
            // Empty string clears the field; null leaves it unchanged.
            static string? Norm(string? v) => string.IsNullOrWhiteSpace(v) ? null : v.Trim();
            if (r.FirstName           is not null) profile.FirstName           = Norm(r.FirstName);
            if (r.LastName            is not null) profile.LastName            = Norm(r.LastName);
            if (r.Designation         is not null) profile.Designation         = Norm(r.Designation);
            if (r.EmploymentType      is not null) profile.EmploymentType      = Norm(r.EmploymentType);
            if (r.PanNumber           is not null) profile.PanNumber           = Norm(r.PanNumber)?.ToUpperInvariant();
            if (r.AadhaarNumberMasked is not null) profile.AadhaarNumberMasked = Norm(r.AadhaarNumberMasked);
            if (r.BankAccountName     is not null) profile.BankAccountName     = Norm(r.BankAccountName);
            if (r.BankAccountNumber   is not null) profile.BankAccountNumber   = Norm(r.BankAccountNumber);
            if (r.BankIfsc            is not null) profile.BankIfsc            = Norm(r.BankIfsc)?.ToUpperInvariant();
            if (r.UpiId               is not null) profile.UpiId               = Norm(r.UpiId);
            if (r.KycStatus is not null)
            {
                var next = Norm(r.KycStatus)?.ToLowerInvariant();
                // Stamp verified-at on the pending→verified transition; clear it otherwise.
                if (next == "verified" && profile.KycStatus != "verified") profile.KycVerifiedAt = DateTimeOffset.UtcNow;
                else if (next != "verified") profile.KycVerifiedAt = null;
                profile.KycStatus = next;
            }
            profile.UpdatedAt = DateTimeOffset.UtcNow; profile.UpdatedBy = cmd.ActorId;
        }

        await _db.SaveChangesAsync(ct);

        var result = new UserDto(user.Id, user.Email, user.PhoneE164, user.UserType, user.Status,
            user.MfaEnabled, user.LastLoginAt, user.CreatedAt,
            profile?.FirstName, profile?.LastName, profile?.DisplayName,
            profile?.Designation, profile?.EmploymentType, profile?.PanNumber, profile?.AadhaarNumberMasked,
            profile?.KycStatus, profile?.KycVerifiedAt, profile?.BankAccountName, profile?.BankAccountNumber,
            profile?.BankIfsc, profile?.UpiId);

        // Mask financial PII unless the caller holds users.read_financial.
        // The edit drawer uses "blank = keep, typed = overwrite" semantics and never echoes
        // sensitive fields back — this masking is consistent with that contract.
        return UserDtoFinancialMask.Apply(result, _actor);
    }
}
