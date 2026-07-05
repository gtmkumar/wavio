using WaAdmin.Application.Consent.Dtos;
using Wavio.Utilities.CQRS.Abstractions;

namespace WaAdmin.Application.Consent.Commands.RecordOptIn;

/// <summary>POST /v1/consent/opt-in — writes one append-only consent.opt_in_events row (issue
/// #21, spec §4.10). <see cref="SourceIp"/> comes from the HTTP request (endpoint reads
/// <c>HttpContext.Connection.RemoteIpAddress</c>), not the request body — a caller cannot spoof
/// its own source_ip.</summary>
public sealed record RecordOptInCommand(
    RecordOptInRequest Request, Guid TenantId, Guid? ActorId, System.Net.IPAddress? SourceIp)
    : ICommand<OptInEventDto>;
