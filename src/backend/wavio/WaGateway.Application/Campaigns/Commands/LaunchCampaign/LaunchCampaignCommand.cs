using WaGateway.Application.Campaigns.Dtos;
using Wavio.Utilities.CQRS.Abstractions;

namespace WaGateway.Application.Campaigns.Commands.LaunchCampaign;

/// <summary><c>POST /v1/campaigns/{id}/launch</c> (issue #22). Transitions draft/scheduled -&gt;
/// running; the actual per-recipient dispatch happens later, out of request scope, in
/// <c>CampaignChunkerService</c> (WaGateway.Infrastructure) — same "accept synchronously, dispatch
/// asynchronously" split as the outbox pattern (issue #14).</summary>
public sealed record LaunchCampaignCommand(Guid TenantId, Guid CampaignId) : ICommand<CampaignDto>;
