using WaIntel.Application.Windows.Dtos;
using Wavio.Utilities.CQRS.Abstractions;

namespace WaIntel.Application.Windows.Queries.GetWindowState;

/// <summary>
/// <c>GET /v1/windows/{waId}</c> (spec §7.1). <paramref name="PhoneNumberId"/> is an optional
/// disambiguator for the (currently rare) multi-number-per-tenant case (spec §4.1) — the cache
/// key and the common lookup path key off (TenantId, UserWaId) only, matching the Wave 1 reality
/// of one connected number per tenant; see <see cref="GetWindowStateHandler.BuildCacheKey"/>.
/// </summary>
public sealed record GetWindowStateQuery(
    Guid TenantId,
    string UserWaId,
    Guid? PhoneNumberId) : IQuery<WindowStateDto?>;
