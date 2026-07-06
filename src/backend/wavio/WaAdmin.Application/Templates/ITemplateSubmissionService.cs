using wavio.SharedDataModel.Entities.Templates;

namespace WaAdmin.Application.Templates;

/// <summary>Outcome of a submit attempt. <see cref="Submitted"/> false means the template stays
/// exactly where it was (DRAFT) — never a partial/ambiguous state — with <see cref="Error"/>
/// describing why, so the caller can retry.</summary>
public sealed record TemplateSubmissionOutcome(bool Submitted, string? Error);

/// <summary>
/// Lints (issue #27 — every registered <see cref="WaAdmin.Application.Common.Interfaces.ITemplateLintService"/>
/// runs here, gating on any blocking finding) and submits a DRAFT template version to Meta, then
/// on acceptance performs the app-enforced DRAFT -&gt; PENDING transition (recording a
/// <see cref="TemplateStatusEvent"/>). Shared between the create-and-submit flow
/// (POST /v1/templates) and the standalone resubmit flow (POST /v1/templates/{id}/submit) so both
/// the lint gate and the transition logic live in exactly one place.
/// </summary>
public interface ITemplateSubmissionService
{
    Task<TemplateSubmissionOutcome> SubmitAsync(
        Template templateToSubmit, TemplateVersion version, Guid? actorId, CancellationToken cancellationToken);
}
