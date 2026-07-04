using WaPlatform.Contracts.IntegrationEvents.V1;
using Wavio.Utilities.CQRS.Abstractions;

namespace WaAdmin.Application.Templates.Commands.ProcessTemplateStatusChanged;

/// <summary>
/// Consumes <c>wa.template.status_changed.v1</c> (published by wa-ingest-svc) and applies the
/// resulting local state transition (spec §4.4, issue #16 Task 3). Returns false — "parked", not
/// an error — when the event cannot be safely applied: an unresolvable tenant (Wave 1 may carry
/// <see cref="WaPlatform.Contracts.IntegrationEvents.IntegrationEvent.TenantId"/> =
/// <see cref="Guid.Empty"/>, per the same caveat WaIngest documents), an unknown template, or an
/// invalid transition. The consumer (WaAdmin.Infrastructure) nacks a parked message to the
/// dead-letter queue rather than fabricating data or dropping it silently.
/// </summary>
public sealed record ProcessTemplateStatusChangedCommand(TemplateStatusChangedV1 Event) : ICommand<bool>;
