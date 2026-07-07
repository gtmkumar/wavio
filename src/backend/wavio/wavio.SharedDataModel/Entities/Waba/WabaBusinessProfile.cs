namespace wavio.SharedDataModel.Entities.Waba;

/// <summary>
/// waba.business_profiles (db/migrations/V002__waba.sql) — the public WhatsApp business profile,
/// 1:1 per phone number (unique on <see cref="PhoneNumberId"/>). The onboarding wizard's profile
/// step writes it to Meta first, then mirrors it here so the console can render the profile
/// without a Graph round-trip (docs/ONBOARDING_WIZARD_PLAN.md).
/// </summary>
public class WabaBusinessProfile
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid PhoneNumberId { get; set; }
    public string? About { get; set; }
    public string? Address { get; set; }
    public string? Description { get; set; }
    public string? Email { get; set; }
    public string[] Websites { get; set; } = [];
    public string? Vertical { get; set; }
    public string? ProfilePictureUrl { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public Guid? CreatedBy { get; set; }
    public Guid? UpdatedBy { get; set; }
    public int Version { get; set; }
}
