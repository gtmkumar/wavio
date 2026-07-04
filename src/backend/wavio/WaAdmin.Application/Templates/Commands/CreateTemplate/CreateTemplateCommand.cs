using WaAdmin.Application.Templates.Dtos;
using Wavio.Utilities.CQRS.Abstractions;

namespace WaAdmin.Application.Templates.Commands.CreateTemplate;

/// <summary>POST /v1/templates — create (DRAFT), lint, and attempt an immediate submit to Meta
/// (spec §7.1, issue #16 Task 1). See <see cref="CreateTemplateResult"/> for why submit failure
/// does not fail the whole request.</summary>
public sealed record CreateTemplateCommand(CreateTemplateRequest Request, Guid TenantId, Guid? ActorId)
    : ICommand<CreateTemplateResult>;
