using WaIntel.Application.Common.Interfaces;
using WaIntel.Application.Quality.Dtos;
using WaIntel.Application.Quality.Logic;
using Wavio.Utilities.CQRS.Abstractions;
using Microsoft.EntityFrameworkCore;

namespace WaIntel.Application.Quality.Queries.GetTierAdvisor;

/// <summary>
/// Feeds <see cref="TierRules.ComputeSafeDailySendPlan"/> with the number's current tier/rating
/// (from <c>waba.phone_numbers</c>) and recent volume (from its latest <c>health_snapshots</c> row
/// — <c>messages_sent</c> is a weekly total, so daily average = that / 7). No snapshot yet (first
/// week) falls back to 0 volume with the plan's own "not ready to grow" recommendation covering it.
/// </summary>
public sealed class GetTierAdvisorHandler : IQueryHandler<GetTierAdvisorQuery, TierAdvisorDto?>
{
    private readonly IWaIntelDbContext _db;

    public GetTierAdvisorHandler(IWaIntelDbContext db) => _db = db;

    public async Task<TierAdvisorDto?> HandleAsync(GetTierAdvisorQuery query, CancellationToken cancellationToken)
    {
        var phoneNumber = await _db.WabaPhoneNumbers.AsNoTracking()
            .FirstOrDefaultAsync(p => p.Id == query.PhoneNumberId && p.TenantId == query.TenantId, cancellationToken);

        if (phoneNumber is null || !QualityCodes.TryNormalizeTier(phoneNumber.MessagingTier, out var canonicalTier))
        {
            return null;
        }

        var latestSnapshot = await _db.HealthSnapshots.AsNoTracking()
            .Where(s => s.PhoneNumberId == query.PhoneNumberId)
            .OrderByDescending(s => s.PeriodStart)
            .FirstOrDefaultAsync(cancellationToken);

        var recentAverageDailyVolume = latestSnapshot is null ? 0 : latestSnapshot.MessagesSent / 7;
        var canonicalRating = QualityCodes.NormalizeRating(phoneNumber.QualityRating);

        var plan = TierRules.ComputeSafeDailySendPlan(canonicalTier, recentAverageDailyVolume, canonicalRating);

        return new TierAdvisorDto(
            query.PhoneNumberId, plan.CurrentTier, plan.NextTier, plan.CurrentDailyLimit,
            recentAverageDailyVolume, plan.RecommendedDailyVolume, plan.ReadyToGrow, plan.Recommendation);
    }
}
