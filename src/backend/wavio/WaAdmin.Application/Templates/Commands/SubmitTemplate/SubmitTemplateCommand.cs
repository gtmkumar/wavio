using WaAdmin.Application.Templates.Dtos;
using Wavio.Utilities.CQRS.Abstractions;

namespace WaAdmin.Application.Templates.Commands.SubmitTemplate;

/// <summary>POST /v1/templates/{id}/submit — (re)submit the current DRAFT version to Meta.
/// Reuses the same <see cref="ITemplateSubmissionService"/> the create flow calls inline, so a
/// template edited after rejection (or a fresh draft that failed lint at create time and was
/// fixed) can be resubmitted without going through create again.</summary>
public sealed record SubmitTemplateCommand(Guid TemplateId, Guid TenantId, Guid? ActorId)
    : ICommand<CreateTemplateResult>;
