using WaAdmin.Application.Consent.Dtos;
using Wavio.Utilities.CQRS.Abstractions;

namespace WaAdmin.Application.Consent.Commands.RecordOptOut;

/// <summary>
/// Shared internal command behind BOTH the manual opt-out endpoint (POST /v1/consent/opt-out,
/// reason manual|complaint) and the STOP-keyword listener consumer (reason stop_keyword,
/// keyword/language/inboundWamid populated) — same "one command, two entry points" shape as
/// WaIntel's quality-event handlers (issue #20). Writes consent.opt_out_events AND upserts
/// messaging.suppression_list in the SAME unit of work (spec §4.10).
/// </summary>
public sealed record RecordOptOutCommand(
    Guid TenantId,
    string WaId,
    string Scope,
    string Reason,
    string? Keyword,
    string? Language,
    string? InboundWamid,
    string? PayloadJson,
    Guid? ActorId)
    : ICommand<OptOutEventDto>;
