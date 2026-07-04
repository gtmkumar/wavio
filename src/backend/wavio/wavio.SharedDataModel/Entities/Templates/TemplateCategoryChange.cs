namespace wavio.SharedDataModel.Entities.Templates;

/// <summary>
/// Meta-initiated category reclassification (templates.template_category_changes, issue #16,
/// spec §4.4) — e.g. utility -&gt; marketing, which changes the template's per-message price.
/// <see cref="TenantAlertedAt"/> / <see cref="BillingRecalibratedAt"/> are set once each
/// mandatory reaction has run (tenant notification, billing recalibration hook); both are
/// nullable so a partial/retried reaction is visible rather than silently assumed complete.
/// </summary>
public class TemplateCategoryChange
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid TemplateId { get; set; }

    public string OldCategory { get; set; } = null!;
    public string NewCategory { get; set; } = null!;

    public DateTimeOffset OccurredAt { get; set; }

    /// <summary>Set once the tenant-facing alert has been raised (log/event stub in Wave 1 —
    /// no real notification channel exists yet; see ITenantAlertPublisher).</summary>
    public DateTimeOffset? TenantAlertedAt { get; set; }

    /// <summary>Set once the billing recalibration hook has run (no-op in Wave 1; Wave 2 #19
    /// implements the real recalculation via IBillingRecalibrationHook).</summary>
    public DateTimeOffset? BillingRecalibratedAt { get; set; }

    /// <summary>Raw source event payload, for forensics.</summary>
    public string? Payload { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public Guid? CreatedBy { get; set; }
    public Guid? UpdatedBy { get; set; }
}
