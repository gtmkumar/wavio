using WaAdmin.Application.Consent.Dtos;
using Wavio.Utilities.CQRS.Abstractions;

namespace WaAdmin.Application.ErasureRequests.Queries.GetErasureRequestById;

/// <summary>GET /v1/consent/requests/{id} — status of a previously raised erasure/export request.</summary>
public sealed record GetErasureRequestByIdQuery(Guid TenantId, Guid Id) : IQuery<ErasureRequestDto?>;
