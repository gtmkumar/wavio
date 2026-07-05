using WaGateway.Application.Campaigns.Dtos;
using Wavio.Utilities.CQRS.Abstractions;

namespace WaGateway.Application.Campaigns.Commands.CancelCampaign;

/// <summary><c>POST /v1/campaigns/{id}/cancel</c> (issue #22). Every still-'pending' recipient is
/// marked 'cancelled' in the same call — the chunker will never see them again.</summary>
public sealed record CancelCampaignCommand(Guid TenantId, Guid CampaignId) : ICommand<CampaignDto>;
