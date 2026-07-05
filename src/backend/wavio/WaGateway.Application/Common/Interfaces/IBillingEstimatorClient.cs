namespace WaGateway.Application.Common.Interfaces;

/// <summary>Pre-send billable estimate as needed for a campaign's projected spend — a deliberately
/// narrow local shape, not a shared DTO with wa-billing-svc (services don't share Application-layer
/// types across a process boundary, same convention as <see cref="WindowStateResult"/>).</summary>
public sealed record BillingEstimateResult(bool Found, bool Billable, decimal Amount, string Currency);

/// <summary>
/// Consults wa-billing-svc's estimator (spec §4.7, issue #19: <c>GET /v1/costs/estimate</c>)
/// before a campaign is created, to populate <c>campaigns.projected_cost</c>. Implemented in
/// WaGateway.Infrastructure as an HTTP call — the same service-to-service HTTP hop precedent as
/// <see cref="IWindowStateClient"/> (ADR-005's justification: DDD bounded-context ownership, the
/// estimator's rate-card tables are wa-billing-svc's, not read directly here).
///
/// Unlike <see cref="IWindowStateClient"/>, a null/unreachable result here is NOT fail-closed —
/// the estimate is advisory only (spec §4.3: "our estimates are advisory only," the webhook
/// pricing object is the real billing source of truth), so campaign CREATION still succeeds with
/// <c>projected_cost</c>/<c>projected_currency</c> left null, never blocking the tenant from
/// drafting a campaign because the estimator happened to be down.
/// </summary>
public interface IBillingEstimatorClient
{
    Task<BillingEstimateResult?> EstimateAsync(
        string category, string country, Guid phoneNumberId, CancellationToken cancellationToken);
}
