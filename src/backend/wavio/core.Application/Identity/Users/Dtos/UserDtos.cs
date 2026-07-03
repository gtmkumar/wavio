using wavio.SharedDataModel.Crypto;
using wavio.Utilities.Services;

namespace core.Application.Identity.Users.Dtos;

// ─── DTOs ──────────────────────────────────────────────────────────────────

public sealed record UserDto(
    Guid Id, string? Email, string? PhoneE164, string UserType, string Status,
    bool MfaEnabled, DateTimeOffset? LastLoginAt, DateTimeOffset CreatedAt,
    string? FirstName, string? LastName, string? DisplayName,
    string? Designation = null,
    string? EmploymentType = null,
    string? PanNumber = null, string? AadhaarNumberMasked = null,
    string? KycStatus = null, DateTimeOffset? KycVerifiedAt = null,
    string? BankAccountName = null, string? BankAccountNumber = null,
    string? BankIfsc = null, string? UpiId = null);

/// <summary>
/// Applies masking to financial PII fields of a <see cref="UserDto"/>.
/// Callers holding <c>users.read_financial</c> (or platform_admin bypass) receive clear values;
/// all others receive masked values per <see cref="PiiMask"/>.
/// </summary>
internal static class UserDtoFinancialMask
{
    internal const string ReadFinancialPermission = "users.read_financial";

    internal static UserDto Apply(UserDto dto, ICurrentUser actor) =>
        actor.IsPlatformAdmin || actor.HasPermission(ReadFinancialPermission)
            ? dto
            : dto with
            {
                PanNumber         = PiiMask.MaskPan(dto.PanNumber),
                BankAccountNumber = PiiMask.MaskBankAccount(dto.BankAccountNumber),
                UpiId             = PiiMask.MaskUpi(dto.UpiId),
                // IFSC is a public branch code — returned clear regardless of permission.
                // AadhaarNumberMasked: already masked at write time by the frontend;
                // returned as stored.
            };
}

// ─── Request payloads ──────────────────────────────────────────────────────

public sealed record CreateUserRequest(
    string? Email, string? Phone, string UserType,
    string? Password = null,
    string? FirstName = null, string? LastName = null, string? Designation = null);

/// <summary>
/// H3: UserType and Status removed — they are privileged fields.
/// Use POST /deactivate to change status; POST /set-type to change UserType.
/// </summary>
public sealed record UpdateUserRequest(
    string? Email = null,
    string? Phone = null,
    string? FirstName = null,
    string? LastName = null,
    string? Designation = null,
    // Employment & payout details (profile). Send "" to clear, null to leave unchanged.
    string? EmploymentType = null,
    string? PanNumber = null,
    string? AadhaarNumberMasked = null,
    string? KycStatus = null,
    string? BankAccountName = null,
    string? BankAccountNumber = null,
    string? BankIfsc = null,
    string? UpiId = null);

/// <summary>H3: Separate request for changing a user's type; requires users.set_type permission.</summary>
public sealed record SetUserTypeRequest(string NewUserType);

public sealed record SetPasswordRequest(string NewPassword);

// ─── Roles & permissions DTOs (admin roles slice) ──────────────────────────

public sealed record RoleDto(Guid Id, string Code, string Name, string ScopeType, bool IsSystem, string Status);
public sealed record PermissionDto(Guid Id, string Code, string Module, string Action, string Name, string RiskLevel);
public sealed record MembershipDto(Guid Id, Guid UserId, string ScopeType, Guid? ScopeId, Guid RoleId, string RoleCode, bool IsPrimary, DateTimeOffset GrantedAt);

public sealed record AssignPermissionRequest(Guid RoleId, Guid PermissionId);
public sealed record GrantMembershipRequest(Guid UserId, string ScopeType, Guid? ScopeId, Guid RoleId, bool IsPrimary = false);
public sealed record RevokeMembershipRequest(Guid MembershipId, string? Reason = null);
/// <summary>Replace a user's PRIMARY role: grant the new one as primary, revoke the old primary.</summary>
public sealed record ChangeRoleRequest(Guid RoleId, string ScopeType, Guid? ScopeId);
