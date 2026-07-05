using WaIntel.Application.Quality.Dtos;
using Wavio.Utilities.CQRS.Abstractions;

namespace WaIntel.Application.Quality.Queries.GetTierAdvisor;

/// <summary>Tier-growth advisor (spec §4.6) for a single number. Returns null when the number
/// doesn't exist for this tenant or has no messaging tier reported yet.</summary>
public sealed record GetTierAdvisorQuery(Guid TenantId, Guid PhoneNumberId) : IQuery<TierAdvisorDto?>;
