using WaBilling.Application.Common.Interfaces;
using WaBilling.Application.Common.Logic;
using WaBilling.Application.Quotas.Dtos;
using WaBilling.Application.Quotas.Logic;
using wavio.SharedDataModel.Entities.Billing;
using wavio.Utilities.Exceptions;
using Wavio.Utilities.CQRS.Abstractions;
using Microsoft.EntityFrameworkCore;
using ValidationException = wavio.Utilities.Exceptions.ValidationException;

namespace WaBilling.Application.Quotas.Commands.CheckQuota;

public sealed class CheckQuotaCommandHandler : ICommandHandler<CheckQuotaCommand, QuotaCheckResultDto>
{
    // tenant_quotas/usage_counters CHECK-enforced category vocabulary (db/migrations/V010) —
    // 'authentication_international' is a rate-card/ledger-only category, not a quota category.
    private static readonly string[] SendableCategories =
        ["marketing", "utility", "authentication", "service"];

    private readonly IWaBillingDbContext _db;

    public CheckQuotaCommandHandler(IWaBillingDbContext db) => _db = db;

    public async Task<QuotaCheckResultDto> HandleAsync(CheckQuotaCommand command, CancellationToken cancellationToken)
    {
        if (!SendableCategories.Contains(command.Category))
            throw new ValidationException(new Dictionary<string, string[]>
            {
                ["category"] = [$"category must be one of: {string.Join(", ", SendableCategories)}."]
            });

        // Both the send's own category quota AND the tenant-wide "all" aggregate quota apply —
        // either one tripping is enough to alert/block (spec §4.7 allows either shape).
        var quotas = await _db.TenantQuotas
            .Where(q => q.TenantId == command.TenantId && q.Enabled
                     && (q.Category == command.Category || q.Category == "all"))
            .ToListAsync(cancellationToken);

        if (quotas.Count == 0)
        {
            return new QuotaCheckResultDto(
                Allowed: true, Blocked: false, SoftLimitReached: false, HardLimitReached: false,
                Reason: "No quota configured for this tenant/category.");
        }

        var now = DateTimeOffset.UtcNow;
        var softReached = false;
        var hardReached = false;

        foreach (var quota in quotas)
        {
            var periodStart = BillingPeriods.PeriodStart(quota.Period, now);
            var counter = await _db.UsageCounters.FirstOrDefaultAsync(
                u => u.TenantId == command.TenantId && u.Category == quota.Category
                  && u.Period == quota.Period && u.PeriodStart == periodStart,
                cancellationToken);

            var currentValue = QuotaRules.CurrentValue(quota.LimitUnit, counter);
            var softBreached = QuotaRules.IsSoftBreached(quota, currentValue);
            var hardBreached = QuotaRules.IsHardBreached(quota, currentValue);

            if (!softBreached && !hardBreached) continue;

            softReached |= softBreached;
            hardReached |= hardBreached;

            // Stamp on first crossing only (a re-check within the same period must not re-alert).
            // The counter may not exist yet (limit set to 0, or checked before any usage lands) —
            // create it so the stamp has somewhere to live; RecordMessageCostHandler's own upsert
            // is unaffected (same unique key, first-writer-wins on the initial insert).
            counter ??= await GetOrCreateCounterAsync(command.TenantId, quota.Category, quota.Period, periodStart, quota.Currency, now, cancellationToken);

            if (softBreached && counter.SoftLimitAlertedAt is null)
            {
                counter.SoftLimitAlertedAt = now;
                counter.UpdatedAt = now;
                counter.Version += 1;
            }

            var blocksThisSend = QuotaRules.ShouldBlock(command.Category, hardBreached);
            if (blocksThisSend && counter.HardLimitBlockedAt is null)
            {
                counter.HardLimitBlockedAt = now;
                counter.UpdatedAt = now;
                counter.Version += 1;
            }
        }

        await _db.SaveChangesAsync(cancellationToken);

        var blocked = QuotaRules.ShouldBlock(command.Category, hardReached);
        var reason = blocked
            ? "Hard quota limit reached — marketing sends are blocked until the next period."
            : hardReached
                ? "Hard quota limit reached, but this category is never blocked (utility/authentication/service)."
                : softReached
                    ? "Soft quota limit reached — alert raised, send still allowed."
                    : "Within quota.";

        return new QuotaCheckResultDto(
            Allowed: !blocked, Blocked: blocked, SoftLimitReached: softReached, HardLimitReached: hardReached,
            Reason: reason);
    }

    private async Task<UsageCounter> GetOrCreateCounterAsync(
        Guid tenantId, string category, string period, DateOnly periodStart, string? currency,
        DateTimeOffset now, CancellationToken cancellationToken)
    {
        // Re-check under the same key right before insert — SaveChanges hasn't run yet for any
        // earlier iteration's new counter in this loop, but a distinct (category, period) key
        // can't collide with itself, so this is just the standard "don't assume the caller
        // already looked" guard for a private helper.
        var existing = await _db.UsageCounters.FirstOrDefaultAsync(
            u => u.TenantId == tenantId && u.Category == category
              && u.Period == period && u.PeriodStart == periodStart,
            cancellationToken);
        if (existing is not null) return existing;

        var counter = new UsageCounter
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            Category = category,
            Period = period,
            PeriodStart = periodStart,
            Currency = currency ?? "INR",
            CreatedAt = now,
            UpdatedAt = now,
            Version = 1,
        };
        _db.UsageCounters.Add(counter);
        return counter;
    }
}
