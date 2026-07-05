namespace WaAdmin.Application.Consent.Dtos;

/// <summary>
/// POST /v1/consent/opt-in request body. <see cref="WaId"/> is the number consent is captured
/// FOR (the future message-send target / suppression key) — when the consenting PARTY is someone
/// else (spec §4.10 behalf-of pattern: "consenting party ≠ service recipient"; e.g. a receptionist
/// captures consent on behalf of a walk-in customer, or a parent consents for a dependant with no
/// WhatsApp of their own), <see cref="OnBehalfOfWaId"/>/<see cref="OnBehalfOfName"/> record who
/// that other party is. These are explicit typed request fields, not a free-form evidence blob
/// the caller has to hand-construct — the handler folds them into <c>evidence</c> jsonb at write
/// time (V012's schema has no separate on-behalf-of column; it is frozen, see CLAUDE.md).
/// </summary>
public sealed record RecordOptInRequest(
    string WaId,
    string Purpose,
    string CaptureChannel,
    string? OnBehalfOfWaId,
    string? OnBehalfOfName,
    string? EvidenceProofRef,
    string? EvidenceWamid,
    string? Actor);

public sealed record OptInEventDto(
    Guid Id,
    string WaId,
    string Purpose,
    string CaptureChannel,
    string? EvidenceWamid,
    string? Actor,
    DateTimeOffset OccurredAt);

/// <summary>POST /v1/consent/opt-out (manual path only — reason must be manual or complaint;
/// stop_keyword opt-outs are written exclusively by the STOP listener, never through this
/// endpoint, so a caller cannot forge keyword-detection provenance).</summary>
public sealed record RecordManualOptOutRequest(
    string WaId,
    string Scope,
    string Reason,
    string? Notes);

public sealed record OptOutEventDto(
    Guid Id,
    string WaId,
    string Scope,
    string Reason,
    string? Keyword,
    string? Language,
    DateTimeOffset OccurredAt);

public sealed record ConsentPurposeStateDto(
    string Purpose, bool OptedIn, DateTimeOffset? LastOptInAt, DateTimeOffset? LastOptOutAt);

public sealed record ConsentStateDto(
    string WaId, bool Suppressed, IReadOnlyList<ConsentPurposeStateDto> Purposes);

public sealed record CreateErasureRequestRequest(
    string WaId,
    string RequestType,
    string? Reason,
    string? RequestedBy);

public sealed record ErasureRequestDto(
    Guid Id,
    string WaId,
    string RequestType,
    string Status,
    string? Reason,
    DateTimeOffset? ContentErasedAt,
    string? ExportRef,
    DateTimeOffset? CompletedAt,
    DateTimeOffset CreatedAt);

public sealed record RetentionPolicyDto(
    Guid Id,
    Guid? TenantId,
    string DataClass,
    int RetentionDays,
    string? Basis,
    bool Enabled,
    DateTimeOffset UpdatedAt);

/// <summary>PUT /v1/consent/retention-policies — always writes a TENANT OVERRIDE row
/// (<c>tenant_id</c> = the caller's tenant); a tenant caller can never edit the platform-default
/// (NULL-tenant) row through this endpoint.</summary>
public sealed record UpsertRetentionPolicyRequest(
    string DataClass,
    int RetentionDays,
    string? Basis,
    bool Enabled);
