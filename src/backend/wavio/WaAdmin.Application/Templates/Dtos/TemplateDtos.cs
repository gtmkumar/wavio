using WaPlatform.Contracts.TemplateDsl;

namespace WaAdmin.Application.Templates.Dtos;

public sealed record TemplateVersionDto(
    Guid Id,
    int VersionNumber,
    string ComponentsJson,
    string? ExampleValuesJson,
    string Status,
    string? RejectionReason,
    DateTimeOffset? SubmittedAt,
    DateTimeOffset? ReviewedAt,
    DateTimeOffset CreatedAt);

public sealed record TemplateDto(
    Guid Id,
    Guid BusinessAccountId,
    string Name,
    string Language,
    string Category,
    string? MetaTemplateId,
    string Status,
    Guid? CurrentVersionId,
    DateTimeOffset? PausedUntil,
    short PauseCount,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    TemplateVersionDto? CurrentVersion);

public sealed record TemplateStatusEventDto(
    Guid Id,
    string? OldStatus,
    string NewStatus,
    string? Reason,
    DateTimeOffset OccurredAt);

public sealed record TemplateStatusDto(
    Guid TemplateId,
    string Status,
    string? MetaTemplateId,
    DateTimeOffset? PausedUntil,
    short PauseCount,
    IReadOnlyList<TemplateStatusEventDto> History);

/// <summary>Create request: the platform-native DSL (spec-defined, compiled to Meta's component
/// JSON by <see cref="TemplateDefinitionCompiler"/>) plus the platform business-account id the
/// template belongs to.</summary>
public sealed record CreateTemplateRequest(
    Guid BusinessAccountId,
    TemplateDefinition Definition,
    string? ExampleValuesJson);

/// <summary>Update request: replacement DSL content. Immutability is enforced by the handler,
/// not here — see UpdateTemplateHandler.</summary>
public sealed record UpdateTemplateRequest(
    TemplateDefinition Definition,
    string? ExampleValuesJson);

/// <summary>Result of POST /v1/templates: the created (always DRAFT-first) template plus whether
/// the immediate submit-to-Meta step succeeded. Create never fails just because the submit call
/// did — the template row is durable either way and can be resubmitted via POST .../submit.</summary>
public sealed record CreateTemplateResult(
    TemplateDto Template,
    bool SubmittedToMeta,
    string? SubmissionError);

/// <summary>GET /v1/templates/metrics/approval-rate (issue #27, spec §4.4: target &gt;90% first-pass
/// approval). Scoped to whatever templates.template_status_events / template_lint_results rows
/// are visible under RLS for the caller's tenant — same "scoping via RLS, not an explicit filter"
/// posture as GetTemplatesQueryHandler.</summary>
public sealed record TemplateApprovalMetricsDto(
    int ReviewedVersionCount,
    int FirstPassApprovedCount,
    double? FirstPassApprovalRate,
    int LintRunCount,
    int LintPassedCount,
    double? LintPassRate);
