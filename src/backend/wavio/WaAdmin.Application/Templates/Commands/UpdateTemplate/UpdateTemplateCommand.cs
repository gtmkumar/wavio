using WaAdmin.Application.Templates.Dtos;
using Wavio.Utilities.CQRS.Abstractions;

namespace WaAdmin.Application.Templates.Commands.UpdateTemplate;

/// <summary>PUT /v1/templates/{id} — edit content. Immutability (issue #16 Task 6): a DRAFT
/// current version is edited in place; anything else (APPROVED/PAUSED/REJECTED) creates a new
/// DRAFT version instead of mutating the reviewed one. PENDING/DISABLED reject the edit outright
/// (mid-review / terminal).</summary>
public sealed record UpdateTemplateCommand(Guid TemplateId, UpdateTemplateRequest Request, Guid TenantId, Guid? ActorId)
    : ICommand<TemplateDto?>;
