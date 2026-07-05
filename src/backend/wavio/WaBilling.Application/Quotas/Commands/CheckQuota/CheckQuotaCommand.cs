using WaBilling.Application.Quotas.Dtos;
using Wavio.Utilities.CQRS.Abstractions;

namespace WaBilling.Application.Quotas.Commands.CheckQuota;

/// <summary>POST /v1/quotas/check — called at send time (spec §4.7) by whatever is about to
/// dispatch a message of <paramref name="Category"/>. A command, not a query: crossing a
/// threshold for the first time in a period stamps <c>soft_limit_alerted_at</c> /
/// <c>hard_limit_blocked_at</c> on the usage counter so a repeat check doesn't re-alert every
/// call. Does NOT increment usage itself — that happens once the send is actually confirmed
/// billed, via <c>RecordMessageCostCommand</c>.</summary>
public sealed record CheckQuotaCommand(Guid TenantId, string Category) : ICommand<QuotaCheckResultDto>;
