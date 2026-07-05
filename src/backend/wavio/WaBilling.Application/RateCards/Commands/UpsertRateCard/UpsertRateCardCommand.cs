using WaBilling.Application.RateCards.Dtos;
using Wavio.Utilities.CQRS.Abstractions;

namespace WaBilling.Application.RateCards.Commands.UpsertRateCard;

/// <summary>POST /v1/rate-cards (create, RateCardId null) or PUT /v1/rate-cards/{id} (replace
/// header + entries). platform-admin scoped — Meta's rate card is the same for every tenant.</summary>
public sealed record UpsertRateCardCommand(Guid? RateCardId, UpsertRateCardRequest Request, Guid? ActorId)
    : ICommand<RateCardDto>;
