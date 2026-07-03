namespace wavio.SharedDataModel.Entities.IdentityAccess;

/// <summary>Authentication attempt log (identity_access.login_history).
/// Has occurred_at, created_at, created_by — no updated_at, no version, no deleted_at.</summary>
public class LoginHistory
{
    public Guid Id { get; set; }
    public Guid? UserId { get; set; }

    public string Identifier { get; set; } = null!;
    public string AuthMethod { get; set; } = null!;
    public bool Success { get; set; }
    public string? FailureReason { get; set; }
    public IPAddress? IpAddress { get; set; }
    public string? UserAgent { get; set; }
    public string? DeviceId { get; set; }
    public string? CountryCode { get; set; }
    public string? City { get; set; }
    public bool IsSuspicious { get; set; }
    public short? RiskScore { get; set; }
    public DateTimeOffset OccurredAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public Guid? CreatedBy { get; set; }

    // Navigations
    public User? User { get; set; }
}
