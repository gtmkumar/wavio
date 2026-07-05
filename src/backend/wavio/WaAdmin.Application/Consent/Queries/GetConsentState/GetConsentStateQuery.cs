using WaAdmin.Application.Consent.Dtos;
using Wavio.Utilities.CQRS.Abstractions;

namespace WaAdmin.Application.Consent.Queries.GetConsentState;

/// <summary>GET /v1/consent/{waId} — current derived consent state (issue #21, spec §4.10).</summary>
public sealed record GetConsentStateQuery(Guid TenantId, string WaId) : IQuery<ConsentStateDto>;
