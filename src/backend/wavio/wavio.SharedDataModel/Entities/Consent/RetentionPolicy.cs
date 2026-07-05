namespace wavio.SharedDataModel.Entities.Consent;

/// <summary>
/// Configurable data-retention policy row (consent.retention_policies,
/// db/migrations/V012__consent.sql, issue #21, spec §4.10). Nullable-tenant pattern: a NULL
/// <see cref="TenantId"/> row is a platform default visible to every tenant (RLS: "tenant_id IS
/// NULL OR ..."); a non-NULL row is that tenant's override for the same
/// <see cref="DataClass"/>, enforced unique together (NULLS NOT DISTINCT, so at most one
/// platform default per data class can ever exist).
///
/// A retention ENFORCEMENT sweep (actually deleting/archiving rows once they age past
/// <see cref="RetentionDays"/>) is deliberately NOT built in issue #21 — see
/// WaAdmin.Infrastructure's DependencyInjection.cs doc comment / the issue-21 agent-memory note
/// for why (needs a product decision on deletion semantics vs. the erasure workflow already
/// built here).
/// </summary>
public class RetentionPolicy
{
    public Guid Id { get; set; }

    /// <summary>Null = platform default (visible to every tenant). Non-null = this tenant's
    /// override.</summary>
    public Guid? TenantId { get; set; }

    /// <summary>message_content | metadata | cost_ledger | consent_evidence | raw_webhook
    /// (CHECK-enforced).</summary>
    public string DataClass { get; set; } = null!;

    public int RetentionDays { get; set; }

    /// <summary>Free text: why this retention period was chosen (e.g. "dpdp_default",
    /// "tax_retention_8y", "ingest_ttl") — no CHECK constraint, informational.</summary>
    public string? Basis { get; set; }

    public bool Enabled { get; set; } = true;

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public Guid? CreatedBy { get; set; }
    public Guid? UpdatedBy { get; set; }
    public int Version { get; set; }
}
