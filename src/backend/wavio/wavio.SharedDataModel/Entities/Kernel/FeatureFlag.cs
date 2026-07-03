namespace wavio.SharedDataModel.Entities.Kernel;

/// <summary>Feature flag with rollout rules (kernel.feature_flags).
/// Has created_at, updated_at, created_by, updated_by, status — no version, no deleted_at.</summary>
public class FeatureFlag
{
    public Guid Id { get; set; }
    public Guid? TenantId { get; set; }
    public string FlagKey { get; set; } = null!;
    public string Name { get; set; } = null!;
    public string? Description { get; set; }
    public string FlagType { get; set; } = null!;
    public bool DefaultValue { get; set; }
    public bool IsEnabled { get; set; }
    public short? RolloutPercent { get; set; }
    public string[]? TargetSegments { get; set; }
    public Guid[]? TargetUserIds { get; set; }
    public string[]? TargetCities { get; set; }
    public string? Variants { get; set; }
    public DateTimeOffset? StartsAt { get; set; }
    public DateTimeOffset? EndsAt { get; set; }
    public DateTimeOffset? LastEvaluatedAt { get; set; }
    public long EvaluationCount { get; set; }
    public string Metadata { get; set; } = null!;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public Guid? CreatedBy { get; set; }
    public Guid? UpdatedBy { get; set; }
    public string Status { get; set; } = null!;
}
