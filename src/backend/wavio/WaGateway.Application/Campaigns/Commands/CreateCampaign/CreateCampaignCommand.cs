using WaGateway.Application.Campaigns.Dtos;
using Wavio.Utilities.CQRS.Abstractions;

namespace WaGateway.Application.Campaigns.Commands.CreateCampaign;

/// <summary><c>POST /v1/campaigns</c> (spec §4.2/§7.1, issue #22). <paramref name="Country"/>
/// defaults to "IN" when null/blank — see <see cref="CreateCampaignRequest"/>'s doc comment.</summary>
public sealed record CreateCampaignCommand(
    Guid TenantId,
    string Name,
    Guid PhoneNumberId,
    Guid TemplateVersionId,
    string? ParamsJson,
    IReadOnlyList<CampaignAudienceMemberRequest> Audience,
    DateTimeOffset? ScheduledAt,
    string? Country) : ICommand<CampaignDto>;
