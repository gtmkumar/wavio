using WaAdmin.Application.Consent.Dtos;
using Wavio.Utilities.CQRS.Abstractions;

namespace WaAdmin.Application.RetentionPolicies.Queries.GetRetentionPolicies;

/// <summary>GET /v1/consent/retention-policies — the EFFECTIVE policy per data class: this
/// tenant's override row where one exists, else the platform default (NULL-tenant) row.</summary>
public sealed record GetRetentionPoliciesQuery(Guid TenantId) : IQuery<IReadOnlyList<RetentionPolicyDto>>;
