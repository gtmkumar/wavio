using WaBilling.Application.RateCards.Dtos;
using Wavio.Utilities.CQRS.Abstractions;

namespace WaBilling.Application.RateCards.Queries.GetRateCards;

/// <summary>GET /v1/rate-cards — admin listing, optionally filtered by currency/status.</summary>
public sealed record GetRateCardsQuery(string? Currency = null, string? Status = null)
    : IQuery<IReadOnlyList<RateCardDto>>;
