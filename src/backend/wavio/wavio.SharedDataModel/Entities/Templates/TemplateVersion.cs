namespace wavio.SharedDataModel.Entities.Templates;

/// <summary>
/// Immutable-once-approved content snapshot of a <see cref="Template"/>
/// (templates.template_versions, issue #16). A new row is created on every edit made after
/// the previous version left DRAFT (rejected/approved) — never mutate <see cref="Components"/>
/// on a non-DRAFT version. <see cref="Status"/> mirrors the same state-machine values as
/// <see cref="Template.Status"/> but tracks this specific version's own review outcome.
/// </summary>
public class TemplateVersion
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid TemplateId { get; set; }

    /// <summary>1-based, monotonically increasing per template (unique with TemplateId).</summary>
    public int VersionNumber { get; set; }

    /// <summary>Meta template component array as a JSON document string (header/body/footer/buttons).</summary>
    public string Components { get; set; } = null!;

    /// <summary>Example placeholder values submitted alongside components (jsonb), when present.</summary>
    public string? ExampleValues { get; set; }

    /// <summary>DRAFT | PENDING | APPROVED | REJECTED | PAUSED | DISABLED (CHECK-enforced).</summary>
    public string Status { get; set; } = "DRAFT";

    /// <summary>Meta's rejection reason text, set when this version transitions to REJECTED.</summary>
    public string? RejectionReason { get; set; }

    public DateTimeOffset? SubmittedAt { get; set; }
    public DateTimeOffset? ReviewedAt { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public Guid? CreatedBy { get; set; }
    public Guid? UpdatedBy { get; set; }
}
