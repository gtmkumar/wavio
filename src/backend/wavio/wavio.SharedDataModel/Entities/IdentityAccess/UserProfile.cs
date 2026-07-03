namespace wavio.SharedDataModel.Entities.IdentityAccess;

/// <summary>Extended profile for a user (identity_access.user_profiles).
/// PK = user_id (1-to-1 with users). Has created_at, updated_at, created_by, updated_by, status.
/// No version, no deleted_at — do NOT implement IAuditableEntity or ISoftDeletable.</summary>
public class UserProfile
{
    public Guid UserId { get; set; }
    public string? FirstName { get; set; }
    public string? LastName { get; set; }
    public string? DisplayName { get; set; }
    public string? AvatarUrl { get; set; }
    public DateOnly? DateOfBirth { get; set; }
    public string? Gender { get; set; }
    public string? Designation { get; set; }
    public string? Department { get; set; }
    public string? EmployeeId { get; set; }
    public DateOnly? JoinedAt { get; set; }
    public string? EmergencyContactName { get; set; }
    public string? EmergencyContactPhone { get; set; }
    public string? Address { get; set; }

    // Employment & payout details. All optional: never required, available for every person.
    public string? EmploymentType { get; set; }      // full_time | part_time | contractual | consultant | intern
    public string? PanNumber { get; set; }
    public string? AadhaarNumberMasked { get; set; }
    public string? KycStatus { get; set; }            // pending | verified | rejected (null = not started)
    public DateTimeOffset? KycVerifiedAt { get; set; }
    public string? BankAccountName { get; set; }
    public string? BankAccountNumber { get; set; }
    public string? BankIfsc { get; set; }
    public string? UpiId { get; set; }

    public string? FcmToken { get; set; }
    public DateTimeOffset? FcmTokenUpdatedAt { get; set; }
    public string? ApnsToken { get; set; }
    public DateTimeOffset? ApnsTokenUpdatedAt { get; set; }
    public string Preferences { get; set; } = null!;
    public string Metadata { get; set; } = null!;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public Guid? CreatedBy { get; set; }
    public Guid? UpdatedBy { get; set; }
    public string Status { get; set; } = null!;

    // Navigation
    public User User { get; set; } = null!;
}
