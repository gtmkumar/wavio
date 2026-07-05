using WaBilling.Application.Common.Interfaces;
using WaBilling.Application.RateCards.Dtos;
using wavio.SharedDataModel.Entities.Billing;
using wavio.Utilities.Exceptions;
using Wavio.Utilities.CQRS.Abstractions;
using Microsoft.EntityFrameworkCore;

namespace WaBilling.Application.RateCards.Commands.UpsertRateCard;

public sealed class UpsertRateCardCommandHandler : ICommandHandler<UpsertRateCardCommand, RateCardDto>
{
    private readonly IWaBillingDbContext _db;

    public UpsertRateCardCommandHandler(IWaBillingDbContext db) => _db = db;

    public async Task<RateCardDto> HandleAsync(UpsertRateCardCommand command, CancellationToken cancellationToken)
    {
        var request = command.Request;
        var now = DateTimeOffset.UtcNow;

        RateCard? card = command.RateCardId is { } id
            ? await _db.RateCards.Include(c => c.Entries)
                .FirstOrDefaultAsync(c => c.Id == id, cancellationToken)
            : null;

        if (command.RateCardId is not null && card is null)
            throw new BusinessRuleException($"Rate card '{command.RateCardId}' was not found.");

        // Creating a brand-new card into a (currency, effective_from) slot that's already taken
        // is a caller error, not a database surprise — the unique constraint would reject it
        // anyway, but a clear 422 beats a raw constraint-violation 500.
        if (card is null)
        {
            var conflict = await _db.RateCards.AsNoTracking().AnyAsync(
                c => c.Currency == request.Currency && c.EffectiveFrom == request.EffectiveFrom,
                cancellationToken);
            if (conflict)
                throw new BusinessRuleException(
                    $"A rate card for currency '{request.Currency}' effective '{request.EffectiveFrom:O}' already exists — update it instead of creating a new one.");

            card = new RateCard
            {
                Id = Guid.NewGuid(),
                CreatedAt = now,
                CreatedBy = command.ActorId,
                Version = 1,
            };
            _db.RateCards.Add(card);
        }
        else
        {
            card.Version += 1;
        }

        card.Name = request.Name;
        card.Currency = request.Currency;
        card.Source = request.Source;
        card.EffectiveFrom = request.EffectiveFrom;
        card.EffectiveTo = request.EffectiveTo;
        card.Status = request.Status;
        card.Notes = request.Notes;
        card.UpdatedAt = now;
        card.UpdatedBy = command.ActorId;

        // Full replace of the entry set — simplest correct "upsert" for a small, admin-edited
        // table (no per-entry diffing/merge). Existing entries are removed (cascade FK, so this
        // is a plain delete-then-insert within the same unit of work) and replaced wholesale.
        foreach (var existingEntry in card.Entries.ToList())
            _db.RateCardEntries.Remove(existingEntry);

        var newEntries = request.Entries.Select(entryRequest => new RateCardEntry
        {
            Id = Guid.NewGuid(),
            RateCardId = card.Id,
            Category = entryRequest.Category,
            Market = entryRequest.Market,
            VolumeTier = entryRequest.VolumeTier,
            PricePerMessage = entryRequest.PricePerMessage,
            Currency = request.Currency,
            CreatedAt = now,
            UpdatedAt = now,
            CreatedBy = command.ActorId,
            UpdatedBy = command.ActorId,
            Version = 1,
        }).ToList();
        _db.RateCardEntries.AddRange(newEntries);

        await _db.SaveChangesAsync(cancellationToken);

        return new RateCardDto(
            card.Id, card.Name, card.Currency, card.Source, card.EffectiveFrom, card.EffectiveTo,
            card.Status, card.Notes,
            newEntries.Select(e => new RateCardEntryDto(
                e.Id, e.Category, e.Market, e.VolumeTier, e.PricePerMessage, e.Currency)).ToList());
    }
}
