using WaPlatform.Contracts.IntegrationEvents.V1;
using Wavio.Utilities.CQRS.Abstractions;

namespace WaAdmin.Application.Templates.Commands.ProcessTemplateCategoryChanged;

/// <summary>
/// Consumes <c>wa.template.category_changed.v1</c> (published by wa-ingest-svc): records the
/// reclassification, raises a tenant alert, and runs the (Wave 2, no-op) billing recalibration
/// hook (spec §4.4, issue #16 Task 4). Same Guid.Empty tenant caveat as
/// <see cref="ProcessTemplateStatusChanged.ProcessTemplateStatusChangedCommand"/> — returns false
/// ("parked") rather than fabricating a tenant.
/// </summary>
public sealed record ProcessTemplateCategoryChangedCommand(TemplateCategoryChangedV1 Event) : ICommand<bool>;
