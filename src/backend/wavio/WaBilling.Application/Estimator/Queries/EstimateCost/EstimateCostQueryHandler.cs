using WaBilling.Application.Common.Interfaces;
using WaBilling.Application.Estimator.Dtos;
using WaBilling.Application.RateCards.Dtos;
using WaBilling.Application.RateCards.Logic;
using wavio.Utilities.Exceptions;
using Wavio.Utilities.CQRS.Abstractions;
using Microsoft.EntityFrameworkCore;
using ValidationException = wavio.Utilities.Exceptions.ValidationException;

namespace WaBilling.Application.Estimator.Queries.EstimateCost;

public sealed class EstimateCostQueryHandler : IQueryHandler<EstimateCostQuery, CostEstimateDto>
{
    // Single-market v1 (spec §4.7: "INR for India") — every rate card loaded so far is INR.
    // Revisit once a second currency/market is onboarded; nothing else in this handler assumes
    // INR specifically, only this one constant would need to become a per-tenant lookup.
    private const string DefaultCurrency = "INR";

    private readonly IWaBillingDbContext _db;

    public EstimateCostQueryHandler(IWaBillingDbContext db) => _db = db;

    public async Task<CostEstimateDto> HandleAsync(EstimateCostQuery query, CancellationToken cancellationToken)
    {
        if (!RateCardCategories.All.Contains(query.Category))
            throw new ValidationException(new Dictionary<string, string[]>
            {
                ["category"] = [$"category must be one of: {string.Join(", ", RateCardCategories.All)}."]
            });

        if (string.IsNullOrWhiteSpace(query.Country))
            throw new ValidationException(new Dictionary<string, string[]>
            {
                ["country"] = ["country is required."]
            });

        // Free-form reply inside an open service window is always free, independent of category
        // (spec §4.7) — no rate-card lookup needed at all.
        if (query.WindowOpen)
        {
            return new CostEstimateDto(
                Found: true, Billable: false, Amount: 0, Currency: DefaultCurrency,
                Category: query.Category, Market: query.Country, VolumeTier: null, RateCardId: null,
                Reason: "Open customer-service window — free-form reply is free.");
        }

        // Marketing never has volume discounts (spec §4.7) — only utility/authentication look up
        // a tier, and only when the caller identifies a phone number this tenant actually owns.
        string? volumeTier = null;
        if (query.Category != RateCardCategories.Marketing && query.PhoneNumberId is { } phoneNumberId)
        {
            var phoneNumber = await _db.WabaPhoneNumbers.AsNoTracking()
                .FirstOrDefaultAsync(p => p.Id == phoneNumberId && p.TenantId == query.TenantId, cancellationToken);
            volumeTier = phoneNumber?.MessagingTier;
        }

        var asOf = DateOnly.FromDateTime(DateTime.UtcNow);
        var candidateCards = await _db.RateCards.AsNoTracking()
            .Where(c => c.Currency == DefaultCurrency)
            .Include(c => c.Entries)
            .ToListAsync(cancellationToken);

        var card = RateCardSelector.SelectActiveCard(candidateCards, asOf);
        if (card is null)
        {
            return new CostEstimateDto(
                Found: false, Billable: true, Amount: 0, Currency: DefaultCurrency,
                Category: query.Category, Market: query.Country, VolumeTier: volumeTier, RateCardId: null,
                Reason: $"No active rate card for {DefaultCurrency} as of {asOf:O}.");
        }

        var entry = RateCardSelector.SelectEntry(card.Entries, query.Category, query.Country, volumeTier);
        if (entry is null)
        {
            return new CostEstimateDto(
                Found: false, Billable: true, Amount: 0, Currency: card.Currency,
                Category: query.Category, Market: query.Country, VolumeTier: volumeTier, RateCardId: card.Id,
                Reason: $"Rate card '{card.Name}' has no priced entry for category '{query.Category}' / market '{query.Country}'.");
        }

        return new CostEstimateDto(
            Found: true, Billable: true, Amount: entry.PricePerMessage, Currency: entry.Currency,
            Category: query.Category, Market: query.Country, VolumeTier: entry.VolumeTier, RateCardId: card.Id,
            Reason: "Estimated from active rate card.");
    }
}
