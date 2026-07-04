namespace WaAdmin.Application.Common.Interfaces;

/// <summary>Request to submit one template version to Meta's Graph API
/// (<c>POST /{version}/{waba-id}/message_templates</c>).</summary>
public sealed record GraphTemplateSubmitRequest(
    string BusinessAccountMetaId,
    string Name,
    string Language,
    string Category,
    string ComponentsJson);

/// <summary>Result of a submit call. <see cref="MetaTemplateId"/> is populated only when
/// <see cref="Accepted"/> is true (Meta returned 201 with an id).</summary>
public sealed record GraphTemplateSubmitResult(bool Accepted, string? MetaTemplateId, string? ErrorMessage);

/// <summary>
/// Thin client over Meta's WhatsApp Business Management API template endpoints (spec §4.4,
/// issue #16 Task 2). The real system-user access token per WABA is out of scope here — envelope
/// -encrypted per-tenant token storage arrives with onboarding (issue #6); for now the token is a
/// single value read from configuration (<c>Meta:Graph:AccessToken</c>), which is sufficient to
/// exercise the full submit flow against a stub server in dev/tests and will be swapped for the
/// real per-WABA lookup without changing this interface's shape.
/// </summary>
public interface IWhatsAppTemplateGraphClient
{
    Task<GraphTemplateSubmitResult> SubmitTemplateAsync(
        GraphTemplateSubmitRequest request, CancellationToken cancellationToken);
}
