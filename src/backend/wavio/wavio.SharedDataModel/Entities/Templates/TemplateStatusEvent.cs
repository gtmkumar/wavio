namespace wavio.SharedDataModel.Entities.Templates;

/// <summary>
/// Append-only state-machine transition log (templates.template_status_events, issue #16).
/// Written for EVERY <see cref="Template.Status"/>/<see cref="TemplateVersion.Status"/> change —
/// both locally-triggered (submit, edit-back-to-draft) and webhook-driven (Meta's
/// <c>wa.template.status_changed.v1</c>). Never update or delete a row here; it is the audit
/// trail the acceptance criteria for #16 rely on ("all transitions recorded").
/// </summary>
public class TemplateStatusEvent
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid TemplateId { get; set; }
    public Guid? TemplateVersionId { get; set; }

    /// <summary>Null for the very first DRAFT row creation event, if one is ever recorded.</summary>
    public string? OldStatus { get; set; }
    public string NewStatus { get; set; } = null!;

    /// <summary>Meta rejection/pause reason, or a local reason (e.g. "submitted_to_meta").</summary>
    public string? Reason { get; set; }

    /// <summary>Raw source payload (the integration event or local command), for forensics.</summary>
    public string? Payload { get; set; }

    public DateTimeOffset OccurredAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public Guid? CreatedBy { get; set; }
}
