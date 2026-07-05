---
name: issue-19-billing
description: Issue #19 wa-billing-svc cost & billing engine — what was built, the PMP-vs-rate-card design split, and the ADR-002 contract extension
metadata:
  type: project
---

Built the wa-billing-svc (WaBilling.{Application,Infrastructure,WebApi}) implementation of
[[wavio-workflow-conventions]] issue #19 on top of the existing scaffold (port 5104, gateway
prefix `/billing`, already wired into `wavio.AppHost`/`wavio.Gateway` before this issue started).

**Files**: `wavio.SharedDataModel/Entities/Billing/*` (RateCard, RateCardEntry, MessageCost,
TenantQuota, UsageCounter, InvoiceFeed) + matching `Persistence/Configurations/Billing/*`, DbSets
added to `WavioDbContext`; `WabaPhoneNumber` gained `MessagingTier` (was unmapped before — needed
for volume-tier lookups). `WaBilling.Application/{RateCards,Estimator,Costs,Quotas,Reconciliation}`
each follow the feature-folder CQRS shape (Commands/Queries/Dtos/Logic), no MediatR. Pure logic
lives in `RateCardSelector`, `QuotaRules`, `ReconciliationCalculator`, `BillingPeriods` — all
unit-tested with no I/O. `WaBilling.Infrastructure` has its own `ITenantResolver` copy (same
raw-ADO.NET admin-connection pattern as WaIntel's, issue #15) and a
`MessageStatusConsumerService` consuming `wa.message.status.v1`. `tests/WaBilling.Tests` (56
tests, InMemory EF for handler tests, pure xUnit for logic) mirrors WaIntel.Tests conventions.
Also touched: `core.Infrastructure/Seeders/IdentitySeeder.cs` (6 new `billing.*` permission
codes + role grants), `wavio.slnx` (test project entry).

**Key design decisions**:
1. **Rate cards are estimator-only, never the ledger** (ADR-002, migration V010's own header
   comment). `RecordMessageCostCommandHandler` writes `message_costs.amount` straight from the
   webhook's PMP fields — it never computes a price from `RateCardSelector`. Confusing this would
   have been the single most consequential bug in this feature; re-read the migration comment
   before touching either path again.
2. **`MessageStatusV1` didn't carry PMP pricing data** (only `Billable`/`PricingCategory`/
   `PricingModel`) — extended it additively with `Amount`, `Currency`, `DestinationMarket`,
   `PricingRawJson`, and taught `WaIngest.Application/MetaWebhookNormalizer.NormalizeStatus` to
   populate them from the webhook `pricing` object (nested `{value,currency}` or flat shape, both
   handled; no production PMP payload has been observed yet — same "best-effort" caveat the
   normalizer already uses for `PaymentStatusV1`). This means WaIngest.Application and its test
   file were touched by a WaBilling-owned issue; flagged here so it isn't mistaken for scope creep
   in review.
3. **Meta's own `messaging_tier` is the volume-tier key** — added that column to `WabaPhoneNumber`
   rather than inventing tier thresholds nobody specified. Marketing never passes a tier (spec:
   no volume discounts); estimator ignores any owned phone number's tier for marketing category.
4. **Ledger category vocabulary gap**: Meta's real `pricing.category` includes
   `referral_conversion`, which `billing.message_costs.category`'s CHECK constraint (V010) does
   not accept. `RecordMessageCostCommandHandler` skips (not throws) on any category outside the
   5 CHECK-allowed values — documented as a real schema gap, not silently papered over.
5. **Quota check is a command, not a query** — `CheckQuotaCommand` stamps
   `soft_limit_alerted_at`/`hard_limit_blocked_at` on first crossing (idempotent per period);
   `GetQuotaStatusQuery` is the read-only sibling for the tenant self-service view. Evaluates
   BOTH the send's own category quota AND a tenant-wide `category='all'` aggregate quota — either
   tripping is enough to alert/block. The never-block rule (`QuotaRules.ShouldBlock`) is applied
   AFTER the breach check, gated on `category == "marketing"` regardless of which quota row
   triggered the breach — this is the one rule a future editor must not invert.
6. **Idempotent cost insert**: check-then-insert on `Wamid` (same idiom as WaIngest's
   `WebhookProcessor` against `ingest.webhook_dedupe` — no established ON CONFLICT idiom in this
   codebase's Npgsql/EF combination yet), plus a `catch (DbUpdateException)` re-check as defense
   in depth for a genuinely concurrent redelivery. `await` cannot appear in a catch-filter
   (`when (await ...)` is CS7094) — do the duplicate re-check inside the catch body instead.
7. **CQRS validation pipeline is still dead code** — see [[cqrs-validation-pipeline-is-dead-code]].
   Used `ValidationFilter<T>` + `.AddEndpointFilter<ValidationFilter<UpsertRateCardRequest>>()` at
   the HTTP boundary, manual guard-clause `throw new wavio.Utilities.Exceptions.ValidationException`
   for anything else, matching WaGateway's `SendMessageRequestValidator` convention exactly.

**Verified live** (not just unit tests): booted `WaBilling.WebApi` against the real `waplatform`
Postgres (V001–V012 applied) and the real `wavio-rabbitmq` Docker container — `/health` returned
200, `GET /v1/rate-cards` correctly 401'd (auth wired), and the RabbitMQ management API confirmed
`wa-billing.wa.message.status.v1` was declared durable with an active consumer. `psql \d` on
`billing.rate_cards`/`billing.message_costs`/`waba.phone_numbers` matched the EF configurations
exactly. No fixture rows were left behind (read-only checks only).

**Deliberately out of scope**: `payment_transactions` and `max_price_configs` entities (issue #23
and Wave-3 issue #28 respectively, per the migration's own header comments) — no code touches
those tables yet. A quarterly rate-card refresh *job* wasn't built; the issue's own scope note
says future-dated cards loaded via the admin upsert endpoint already satisfy the calendar
requirement.
