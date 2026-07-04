using Wavio.Utilities.CQRS.Abstractions;

namespace WaAdmin.Application.Templates.Commands.DeleteTemplate;

/// <summary>DELETE /v1/templates/{id} — soft delete. Only a DRAFT template (never submitted, or
/// rejected-and-not-yet-resubmitted) may be deleted; anything Meta has seen (PENDING onward) must
/// be DISABLED through the normal lifecycle instead of removed, so the audit trail
/// (template_status_events) stays meaningful.</summary>
public sealed record DeleteTemplateCommand(Guid TemplateId, Guid TenantId, Guid? ActorId) : ICommand<bool>;
