using WaAdmin.Application.Common.Interfaces;
using WaAdmin.Application.Onboarding.Dtos;
using WaAdmin.Application.Onboarding.Logic;
using Wavio.Utilities.CQRS.Abstractions;

namespace WaAdmin.Application.Onboarding.Queries.GetOnboardingStatus;

/// <summary>GET /v1/onboarding/status — DB-only snapshot (no Graph round-trip); the wizard's
/// resumability source. POST /refresh is the Graph-backed sibling. Tenant scoping comes from
/// RLS (app.tenant_id), not an explicit filter.</summary>
public sealed record GetOnboardingStatusQuery : IQuery<OnboardingStatusDto>;

public sealed class GetOnboardingStatusQueryHandler
    : IQueryHandler<GetOnboardingStatusQuery, OnboardingStatusDto>
{
    private readonly IWaAdminDbContext _db;
    public GetOnboardingStatusQueryHandler(IWaAdminDbContext db) => _db = db;

    public Task<OnboardingStatusDto> HandleAsync(
        GetOnboardingStatusQuery query, CancellationToken cancellationToken) =>
        OnboardingSnapshot.LoadAsync(_db, cancellationToken);
}
