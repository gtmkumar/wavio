using wavio.SharedDataModel.Common;

namespace wavio.SharedDataModel.Entities.Templates;

/// <summary>
/// One logical WhatsApp message template per (business_account, name, language)
/// (templates.templates, db/migrations/V009__templates.sql, issue #16, spec §4.4).
///
/// <see cref="Status"/> is the app-enforced state machine root:
/// DRAFT -&gt; PENDING -&gt; APPROVED | REJECTED; APPROVED -&gt; PAUSED -&gt; DISABLED;
/// REJECTED -&gt; DRAFT (edit). Every transition is recorded in
/// <see cref="TemplateStatusEvent"/> — never mutate <see cref="Status"/> without also
/// writing one (see WaAdmin.Application's TemplateStatusTransitions).
///
/// Immutable post-approval: once a version is APPROVED, edits create a new
/// <see cref="TemplateVersion"/> row rather than mutating the approved one — campaigns
/// (Wave 2, #22) pin a specific <see cref="TemplateVersion"/> by id, not the logical template.
/// </summary>
public class Template : IAuditableEntity, ISoftDeletable
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }

    /// <summary>waba.business_accounts.id — no EF navigation (that schema has no entity yet);
    /// the DB foreign key still enforces referential integrity.</summary>
    public Guid BusinessAccountId { get; set; }

    /// <summary>Meta template name — lowercase snake_case, unique per (business account, language).</summary>
    public string Name { get; set; } = null!;

    /// <summary>BCP-47 / Meta language code (e.g. en, en_US, hi).</summary>
    public string Language { get; set; } = null!;

    /// <summary>marketing | utility | authentication (CHECK-enforced) — determines per-message
    /// price; Meta may reclassify this post-approval (see <see cref="TemplateCategoryChange"/>).</summary>
    public string Category { get; set; } = null!;

    /// <summary>Meta-assigned template id, set once the first submission is accepted (201).</summary>
    public string? MetaTemplateId { get; set; }

    /// <summary>DRAFT | PENDING | APPROVED | REJECTED | PAUSED | DISABLED (CHECK-enforced).</summary>
    public string Status { get; set; } = "DRAFT";

    /// <summary>The version currently live/being reviewed. Null until the first version is created.</summary>
    public Guid? CurrentVersionId { get; set; }

    /// <summary>Set when Meta auto-pauses the template (quality); cleared on unpause/disable.</summary>
    public DateTimeOffset? PausedUntil { get; set; }

    /// <summary>Escalation counter for Meta's auto-pause policy: 1st pause 3h, 2nd 6h, 3rd -&gt; DISABLED.</summary>
    public short PauseCount { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public Guid? CreatedBy { get; set; }
    public Guid? UpdatedBy { get; set; }
    public int Version { get; set; }
    public DateTimeOffset? DeletedAt { get; set; }
}
