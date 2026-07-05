namespace WaGateway.Application.Campaigns.Dtos;

/// <summary>One audience member for <c>POST /v1/campaigns</c> — <paramref name="ParamsJson"/> is
/// this recipient's template variable/component override (see <c>Campaign.Params</c>'s doc
/// comment), null to use the campaign-level default for every recipient.</summary>
public sealed record CampaignAudienceMemberRequest(string WaId, string? ParamsJson);

/// <summary>HTTP request body for <c>POST /v1/campaigns</c>. <paramref name="Country"/> defaults to
/// "IN" (single-market v1, same judgment call as WaBilling's estimator hardcoding INR — issue #19)
/// and only feeds the pre-launch spend estimate; it is not stored per recipient.</summary>
public sealed record CreateCampaignRequest(
    string Name,
    Guid PhoneNumberId,
    Guid TemplateVersionId,
    string? ParamsJson,
    IReadOnlyList<CampaignAudienceMemberRequest> Audience,
    DateTimeOffset? ScheduledAt,
    string? Country);

public sealed record CampaignDto(
    Guid Id,
    string Name,
    Guid PhoneNumberId,
    Guid TemplateVersionId,
    string Status,
    DateTimeOffset? ScheduledAt,
    DateTimeOffset? StartedAt,
    DateTimeOffset? CompletedAt,
    int AudienceCount,
    int SuppressedCount,
    int SentCount,
    int DeliveredCount,
    int ReadCount,
    int FailedCount,
    decimal? ProjectedCost,
    string? ProjectedCurrency,
    IReadOnlyDictionary<string, int>? FailureBreakdown);

public sealed record CampaignListItemDto(
    Guid Id,
    string Name,
    string Status,
    int AudienceCount,
    int SentCount,
    int DeliveredCount,
    int ReadCount,
    int FailedCount,
    DateTimeOffset CreatedAt);
