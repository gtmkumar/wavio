using WaBilling.Application.Estimator.Dtos;
using Wavio.Utilities.CQRS.Abstractions;

namespace WaBilling.Application.Estimator.Queries.EstimateCost;

/// <summary>GET /v1/costs/estimate?category=&amp;country=&amp;windowOpen=&amp;phoneNumberId= —
/// pre-send billable estimate (spec §4.7). <paramref name="PhoneNumberId"/> is optional and, when
/// given and owned by <paramref name="TenantId"/>, supplies Meta's own messaging-tier code for
/// utility/authentication volume-tier pricing (marketing never uses a tier — spec: no volume
/// discounts). <paramref name="WindowOpen"/>: true means this is a free-form reply inside an open
/// customer-service window, which is always free regardless of category (spec: "free when an
/// open service window makes the send free").</summary>
public sealed record EstimateCostQuery(
    Guid TenantId, string Category, string Country, bool WindowOpen, Guid? PhoneNumberId)
    : IQuery<CostEstimateDto>;
