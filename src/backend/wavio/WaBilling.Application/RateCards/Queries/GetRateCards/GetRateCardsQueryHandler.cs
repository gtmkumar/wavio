using WaBilling.Application.Common.Interfaces;
using WaBilling.Application.RateCards.Dtos;
using Wavio.Utilities.CQRS.Abstractions;
using Microsoft.EntityFrameworkCore;

namespace WaBilling.Application.RateCards.Queries.GetRateCards;

public sealed class GetRateCardsQueryHandler : IQueryHandler<GetRateCardsQuery, IReadOnlyList<RateCardDto>>
{
    private readonly IWaBillingDbContext _db;

    public GetRateCardsQueryHandler(IWaBillingDbContext db) => _db = db;

    public async Task<IReadOnlyList<RateCardDto>> HandleAsync(GetRateCardsQuery query, CancellationToken cancellationToken)
    {
        var cards = _db.RateCards.AsNoTracking().Include(c => c.Entries).AsQueryable();

        if (!string.IsNullOrWhiteSpace(query.Currency))
            cards = cards.Where(c => c.Currency == query.Currency);
        if (!string.IsNullOrWhiteSpace(query.Status))
            cards = cards.Where(c => c.Status == query.Status);

        var results = await cards.OrderByDescending(c => c.EffectiveFrom).ToListAsync(cancellationToken);

        return results.Select(c => new RateCardDto(
            c.Id, c.Name, c.Currency, c.Source, c.EffectiveFrom, c.EffectiveTo, c.Status, c.Notes,
            c.Entries.Select(e => new RateCardEntryDto(
                e.Id, e.Category, e.Market, e.VolumeTier, e.PricePerMessage, e.Currency)).ToList())).ToList();
    }
}
