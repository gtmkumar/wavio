namespace WaIntel.Application.Windows.Dtos;

/// <summary>Response shape for <c>GET /v1/windows/{waId}</c> (spec §7.1) and the fast-lookup cache value.</summary>
public sealed record WindowStateDto(
    string WaId,
    Guid PhoneNumberId,
    string Origin,
    DateTimeOffset? CsExpiresAt,
    bool CsOpen,
    DateTimeOffset? CtwaExpiresAt,
    bool CtwaOpen);
