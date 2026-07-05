using WaBilling.Application.Common.Interfaces;
using WaBilling.Application.Common.Logic;
using WaBilling.Application.RateCards.Dtos;
using wavio.SharedDataModel.Entities.Billing;
using Wavio.Utilities.CQRS.Abstractions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace WaBilling.Application.Costs.Commands.RecordMessageCost;

/// <summary>
/// Idempotent insert into the PMP cost ledger + usage-counter upsert, in one unit of work (issue
/// #19). Idempotency: check-then-insert on <c>Wamid</c> — the SAME pattern WaIngest's
/// WebhookProcessor uses against <c>ingest.webhook_dedupe</c> (SELECT-check before write, because
/// this codebase's Npgsql/EF Core combination has no established "ON CONFLICT DO NOTHING"
/// idiom yet). Safe under this deployment's single-consumer-instance model; if that ever changes,
/// <c>message_costs_wamid_key</c>'s DB-level UNIQUE constraint still turns a concurrent duplicate
/// insert into a harmless constraint violation, caught below as defense in depth.
/// </summary>
public sealed partial class RecordMessageCostCommandHandler : ICommandHandler<RecordMessageCostCommand, bool>
{
    private readonly IWaBillingDbContext _db;
    private readonly ILogger<RecordMessageCostCommandHandler> _logger;

    public RecordMessageCostCommandHandler(IWaBillingDbContext db, ILogger<RecordMessageCostCommandHandler> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task<bool> HandleAsync(RecordMessageCostCommand command, CancellationToken cancellationToken)
    {
        // This handler runs from the RabbitMQ status consumer (no HttpContext) — see
        // IWaBillingDbContext.SetTenantContextAsync's doc comment for why this explicit GUC set
        // is required before any tenant-scoped read/write on this connection.
        await _db.SetTenantContextAsync(command.TenantId, cancellationToken);

        // Nothing to bill: Meta only attaches a pricing object once it has actually classified
        // the delivery (typically on "delivered", not "sent"/"read") — a status update with no
        // category is legitimately not a billing event, not an error.
        if (command.PricingCategory is null)
        {
            LogSkippedNoPricing(_logger, command.Wamid);
            return false;
        }

        // Known gap (documented, not silently assumed — same house style as MetaWebhookNormalizer's
        // "Known Wave-1 fidelity gaps"): Meta's pricing.category vocabulary includes
        // "referral_conversion" (see MessageStatusV1.Billable doc comment), which
        // billing.message_costs.category's CHECK constraint does not yet accept. Recording that
        // as a ledger row would violate the constraint; until the schema grows a migration for
        // it, these deliveries are skipped here rather than crashing the consumer.
        if (!RateCardCategories.All.Contains(command.PricingCategory))
        {
            LogUnrecognizedCategory(_logger, command.Wamid, command.PricingCategory);
            return false;
        }

        var alreadyRecorded = await _db.MessageCosts.AsNoTracking()
            .AnyAsync(m => m.Wamid == command.Wamid, cancellationToken);
        if (alreadyRecorded)
        {
            LogDuplicate(_logger, command.Wamid);
            return false;
        }

        var now = DateTimeOffset.UtcNow;
        var billable = command.Billable ?? true;
        var amount = billable ? command.Amount ?? 0m : 0m;
        var currency = command.Currency ?? "INR";

        _db.MessageCosts.Add(new MessageCost
        {
            Id = Guid.NewGuid(),
            TenantId = command.TenantId,
            PhoneNumberId = command.PhoneNumberId,
            RateCardId = null, // ledger amount comes from Meta's webhook, never our rate card (ADR-002)
            Wamid = command.Wamid,
            Category = command.PricingCategory,
            PricingModel = command.PricingModel,
            PricingCategory = command.PricingCategory,
            Billable = billable,
            Amount = amount,
            Currency = currency,
            DestinationMarket = command.DestinationMarket,
            WebhookPricing = command.PricingRawJson,
            BilledAt = now,
            CreatedAt = now,
        });

        // Meter both the specific category AND the tenant-wide "all" aggregate — tenant_quotas
        // supports either shape (spec §4.7), and CheckQuotaHandler evaluates both.
        await UpsertUsageCounterAsync(command.TenantId, command.PricingCategory, billable, amount, currency, now, cancellationToken);
        await UpsertUsageCounterAsync(command.TenantId, "all", billable, amount, currency, now, cancellationToken);

        try
        {
            await _db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            // Can't `await` inside a catch filter (CS7094), so re-check unconditionally here.
            // Defense in depth (see class doc comment) — a genuinely concurrent redelivery lost
            // the race to another consumer instance between our check and this insert. Any OTHER
            // cause of a DbUpdateException on this insert is a real failure and must still surface.
            var isDuplicate = await _db.MessageCosts.AsNoTracking()
                .AnyAsync(m => m.Wamid == command.Wamid, cancellationToken);
            if (!isDuplicate) throw;

            LogDuplicate(_logger, command.Wamid);
            return false;
        }

        return true;
    }

    private async Task UpsertUsageCounterAsync(
        Guid tenantId, string category, bool billable, decimal amount, string currency,
        DateTimeOffset now, CancellationToken cancellationToken)
    {
        const string period = BillingPeriods.Monthly; // spec §4.7: "per-tenant monthly message quotas"
        var periodStart = BillingPeriods.PeriodStart(period, now);

        var counter = await _db.UsageCounters.FirstOrDefaultAsync(
            u => u.TenantId == tenantId && u.Category == category
              && u.Period == period && u.PeriodStart == periodStart,
            cancellationToken);

        if (counter is null)
        {
            counter = new UsageCounter
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                Category = category,
                Period = period,
                PeriodStart = periodStart,
                Currency = currency,
                CreatedAt = now,
                UpdatedAt = now,
                Version = 1,
            };
            _db.UsageCounters.Add(counter);
        }

        counter.MessageCount += 1;
        if (billable) counter.BillableAmount += amount;
        counter.UpdatedAt = now;
        counter.Version += 1;
    }

    [LoggerMessage(Level = LogLevel.Debug,
        Message = "wa.message.status.v1 for {Wamid} carried no pricing category — nothing to bill")]
    private static partial void LogSkippedNoPricing(ILogger logger, string wamid);

    [LoggerMessage(Level = LogLevel.Warning,
        Message = "wa.message.status.v1 for {Wamid} reported unrecognized pricing category '{Category}' — skipping ledger insert (schema gap, not a crash)")]
    private static partial void LogUnrecognizedCategory(ILogger logger, string wamid, string category);

    [LoggerMessage(Level = LogLevel.Debug,
        Message = "message_costs row for {Wamid} already exists — skipping (redelivered status webhook)")]
    private static partial void LogDuplicate(ILogger logger, string wamid);
}
