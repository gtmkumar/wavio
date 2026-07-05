using Wavio.Utilities.CQRS.Abstractions;

namespace WaBilling.Application.Costs.Commands.RecordMessageCost;

/// <summary>
/// Consumes one <c>wa.message.status.v1</c> delivery (issue #19, ADR-002) into the PMP cost
/// ledger. Fields map straight off the event's PMP-additive properties — see
/// WaPlatform.Contracts's <c>MessageStatusV1</c> doc comments. Dispatched by
/// WaBilling.Infrastructure's status consumer AFTER tenant resolution (never invoked with an
/// unresolved tenant — see <c>ITenantResolver</c>).
/// </summary>
public sealed record RecordMessageCostCommand(
    Guid TenantId,
    Guid PhoneNumberId,
    string Wamid,
    string? PricingCategory,
    string? PricingModel,
    bool? Billable,
    decimal? Amount,
    string? Currency,
    string? DestinationMarket,
    string? PricingRawJson)
    : ICommand<bool>; // true = a new ledger row was inserted; false = skipped (no pricing / duplicate / unrecognized category)
