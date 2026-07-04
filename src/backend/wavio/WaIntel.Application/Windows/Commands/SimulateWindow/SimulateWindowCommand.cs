using WaIntel.Application.Windows.Dtos;
using Wavio.Utilities.CQRS.Abstractions;

namespace WaIntel.Application.Windows.Commands.SimulateWindow;

/// <summary>
/// QA-only: fabricates an exact window state (issue #15 acceptance criterion). Callers pass the
/// EXACT expiries they want (no +24h/+72h calculation) so QA can construct "about to close",
/// "already closed", "never opened", etc. directly. NEVER reachable in Production — see
/// <see cref="SimulateWindowHandler"/> and the WebApi endpoint's environment gate, both of which
/// independently refuse (fail closed, defense in depth).
/// </summary>
public sealed record SimulateWindowCommand(
    Guid TenantId,
    Guid PhoneNumberId,
    string UserWaId,
    string Origin,
    DateTimeOffset? CsExpiresAt,
    DateTimeOffset? CtwaExpiresAt) : ICommand<WindowStateDto>;
