---
name: issue-20-quality-guardian
description: Issue #20 Quality Rating Guardian (wa-intel-svc) — quality/tier ingestion, auto-throttle, health reports, and two live-discovered auth bugs found during acceptance verification
metadata:
  type: project
---

Built Quality Rating Guardian (spec §4.6) inside **wa-intel-svc** (`WaIntel.{Application,
Infrastructure,WebApi}`), reusing the Windows feature's (issue #15) CQRS/tenant-resolver/consumer
skeleton exactly. `db/migrations/V011__quality.sql` (schema `quality`) and the contract events
(`QualityChangedV1`/`TierChangedV1` in `WaPlatform.Contracts/IntegrationEvents/V1/AccountEvents.cs`)
already existed before this issue (from #23's schema batch and forward-declared in #19) — the
normalizer in WaIngest already published them; nothing consumed them until now.

**Files**: `wavio.SharedDataModel/Entities/Quality/*` (NumberQualityEvent, MessagingTierEvent,
GuardianIncident, HealthSnapshot) + matching `Persistence/Configurations/Quality/*`; `WabaPhoneNumber`
gained `QualityRating` (column existed since V002, unmapped until now — same pattern as #19's
`MessagingTier`). `WaIntel.Application/Quality/{Logic,Dtos,Commands,Queries}` — pure logic in
`QualityCodes` (casing/vocabulary bridge, see below), `GuardianRules`, `TierRules`,
`HealthMetricsRules`, all unit-tested with no I/O. `WaIntel.Infrastructure` gained
`QualityChangedConsumerService`/`TierChangedConsumerService` (mirror `MessageReceivedConsumerService`
exactly) and `HealthSnapshotRollupService` (mirrors `WindowClosingScannerService`'s cross-tenant
scan shape, raw SQL joining `messaging.outbound_messages`/`message_statuses`, `ON CONFLICT
(phone_number_id, period_start) DO NOTHING` as the idempotency claim guard instead of a
notified-at column). `WaIntel.WebApi/Endpoints/Quality.cs`: `GET /health`, `GET
/tier-advisor/{phoneNumberId}`, `POST /simulate` (dev-only + `quality.simulate` permission,
platform_admin-only). `tests/WaIntel.Tests/Quality/*` (108 total WaIntel tests now, up from ~70).
Also touched: `WaGateway.Application/Messages/Logic/GuardianThrottleRules.cs` +
`WaGateway.Infrastructure/RateLimiting/GuardianThrottleGate.cs` (new, mirrors `MessagingTierGate`),
`OutboxDispatcherService.cs` (guardian check wired in), `IWaGatewayDbContext`/`WaGatewayDbContext`
(added `GuardianIncidents` DbSet), `core.Infrastructure/Seeders/IdentitySeeder.cs` (3 new
`quality.*` permission codes).

## Key design decisions

1. **Gateway reads `quality.guardian_incidents` directly, not via pg_notify + cache.** The task
   brief suggested reusing WaIntel's own `WindowCacheInvalidationListener` idiom (LISTEN/NOTIFY +
   `IMemoryCache`) for "propagate within seconds." Deliberately deviated: `OutboxDispatcherService`
   already opens a tenant-scoped DB scope per outbox entry (for `WabaPhoneNumbers`/
   `OutboundMessages`), so one more indexed query on `quality.guardian_incidents` gives ZERO-lag
   correctness — stronger than "within seconds" — without standing up a second LISTEN client
   process in a different service. Documented at both call sites
   (`IWaGatewayDbContext`'s doc comment and the `OutboxDispatcherService` inline comment).
2. **A held-back marketing send is released for retry, not dead-lettered** — both `marketing_50pct`
   (via `GuardianThrottleGate.TryAllowHalvedSend`, a deterministic alternating allow/skip counter
   per phone number, not a probabilistic coin-flip, so tests don't need statistical assertions) and
   `marketing_frozen` use `FencedReleaseForRetryAsync` (1min delay for frozen, 2s for halved) — same
   "throttling is not a failure" treatment as the existing token-bucket rate limiter. A prolonged
   freeze still eventually dead-letters via `MaxAttempts` exhaustion; this was a deliberate choice
   over permanently killing the message, since Guardian's freeze is meant to be temporary
   (resolves back to GREEN).
3. **Casing/vocabulary bridge (`QualityCodes`) is load-bearing, not cosmetic.** Pre-existing schema
   quirk (not introduced here, migrations frozen): `waba.phone_numbers.quality_rating` CHECKs
   UPPERCASE (GREEN/YELLOW/RED/UNKNOWN, V002) while `quality.number_quality_events.new_rating`
   CHECKs lowercase (V011). Similarly `waba.phone_numbers.messaging_tier` stores Meta's raw code
   verbatim (TIER_1K, no CHECK, issue #19 convention) while `quality.messaging_tier_events.new_tier`
   CHECKs a canonical lowercase set (tier_1k, etc.) with DIFFERENT naming than Meta's own codes.
   `QualityCodes.NormalizeRating`/`ToPhoneNumberRatingColumn`/`TryNormalizeTier` is the one place
   that bridges all three representations — re-read it before touching either quality or tier write
   path again, a naive single-casing assumption will violate a live CHECK constraint.
4. **Normalizer's `PreviousRating`/`PreviousTier` (always "UNKNOWN"/null in Wave 1, per its own doc
   comment) is intentionally NOT trusted.** `RecordQualityChangeHandler`/`RecordTierChangeHandler`
   read the number's CURRENTLY STORED rating/tier off `waba.phone_numbers` as the real "old" value
   before diffing — this is exactly the gap the ingest normalizer's doc comment named as "Wave 2
   #20 Guardian owns that state." Also makes redelivery idempotent: no real change → no duplicate
   event row, no duplicate incident (checked live, not just unit-tested).
5. **Health rollup metrics source `messaging.outbound_messages`/`message_statuses` directly** (raw
   SQL, same non-EF pattern as `WindowClosingScannerService`), not a cross-service call to
   WaGateway/WaBilling — same-DB, different-schema reads are the established convention for
   background scanners in this codebase. `messages_failed` counts `outbound_messages.status =
   'failed'` (dispatcher-side failures) — does NOT also count `message_statuses.status='failed'`
   webhook-reported failures, since no consumer writes to that table yet in Wave 1; documented as a
   real undercounting limitation, not silently ignored.
6. **Tier-growth advisor and block-rate-spike detection are documented v1 heuristics**, per the
   task's own "minimal v1" framing — `TierRules.ComputeSafeDailySendPlan`'s 80%-of-limit/1.2x-growth
   numbers and `HealthMetricsRules.BlockRateSpikeThresholdPercent = 15m` are NOT derived from any
   published Meta SLA (none exists) — flagged in both the code doc comments and here so a future
   reader doesn't mistake them for a spec-mandated threshold.

## Two bugs found LIVE during acceptance verification (not by unit tests)

**Bug 1 — `ICurrentUser.Email` always returned null for JWT-authenticated callers**
(`wavio.Utilities/Services/HttpContextCurrentUser.cs`). `JwtTokenService` mints the literal
`"email"` claim, but ASP.NET Core's DEFAULT `JwtSecurityTokenHandler` inbound claim map (nobody's
`AddJwtBearer` call anywhere in the repo sets `MapInboundClaims = false`) silently rewrites
well-known JWT claim names to legacy XML-namespace URIs on the way in — `"email"` becomes
`ClaimTypes.Email`. This made the step-up ("sensitive_action" OTP re-verification) flow
UNUSABLE for every JWT-authenticated user, always failing with "No email on file to verify
against." — meaning no Critical/High-risk permission (any of them, not just this issue's
`quality.simulate`) had ever actually been exercised end-to-end via step-up before. Fixed by
checking both `ClaimTypes.Email` and the literal `"email"` at the one call site — NOT by disabling
`MapInboundClaims` globally, which would have broken `UserId`'s reliance on the SAME default
mapping for `"sub"` → `ClaimTypes.NameIdentifier`. Narrow, two-line fix; every other claim
(`user_type`, `tenant_id`, `scope_type`, `permissions`) is a custom name outside the default map
and was never affected.

**Bug 2 — `Quality.cs`'s own endpoints used `ICurrentTenant.TenantId` (raw JWT claim only),
making the platform_admin-only `/simulate` endpoint impossible to call by the only role permitted
to call it.** `ICurrentTenant.TenantId` reads ONLY the JWT's `tenant_id` claim; a platform_admin
token never carries one (platform admins aren't scoped to a single tenant). Copied this from
Windows.cs's endpoint without noticing Windows' `/simulate` has no permission gate at all (so this
never surfaced there). Fixed by switching to `ICurrentUser.RequireTenantId()` (WaBilling's
`Quotas.cs` convention), which additionally honors the `X-Tenant-Id` header override that
`TenantResolutionMiddleware` sets for platform admins. **Rule of thumb for future endpoints**: if a
permission might ever be platform_admin-only or platform_admin-callable, use `ICurrentUser` +
`RequireTenantId()`, not `ICurrentTenant.TenantId` directly — the latter is fine only for endpoints
exclusively reachable by tenant-scoped callers.

## Verified live (not just unit tests)

Booted `core.WebApi` (5056), `WaIntel.WebApi` (5105), `WaGateway.WebApi` (5101), and
`tools/MetaGraphSendApiStub` (5299) standalone (not via the Aspire AppHost — see
[[aspire-dcp-quirks]]; this dev machine ALSO has a native Homebrew Postgres on :5432 that's the
REAL, actively-migrated `waplatform` database — confusingly, a separate `wavio-postgres` Docker
container also listens on host :5432 but is NOT reachable from the host's own services; only
`docker exec` talks to it. Always confirm via `lsof -iTCP:5432` which process actually owns the
port before trusting a `docker exec psql` check). Logged in as the seeded platform_admin, completed
OTP-based step-up (after Bug 1's fix), created a throwaway tenant-scoped fixture user (copied the
seeded admin's password hash to skip needing the app's hasher) since `ICurrentTenant`-driven
"platform admin calling a tenant-scoped read" isn't supported by this codebase's claim model —
inserted a fixture `waba.business_accounts`/`phone_numbers` row, then:
- `POST /v1/quality/simulate` YELLOW → `quality.guardian_incidents` row opened
  (`quality_yellow`/`marketing_50pct`), `waba.phone_numbers.quality_rating` → `YELLOW`.
- `POST /v1/quality/simulate` RED → the YELLOW incident auto-resolved, a NEW `quality_red`/
  `marketing_frozen` incident opened — both rows verified directly via `psql`.
- Inserted a `messaging.outbound_messages`/`outbound_outbox` row (marketing-category template
  send) for the RED-frozen number: `WaGateway`'s `OutboxDispatcherService` held it back on the very
  next 1s poll tick — `status` stayed `pending`, `attempts` stayed `0`, `next_attempt_at` pushed
  forward exactly 1 minute, ZERO calls reached the Graph stub (confirmed via the stub's own
  request log).
- `POST /v1/quality/simulate` GREEN → resolved the RED incident; reset the outbox entry's
  `next_attempt_at` to `now()` to avoid waiting out the real 1-minute backoff; the very next
  dispatcher tick called the Graph stub, got a `wamid` back, and the entry transitioned to
  `dispatched` — proving the freeze lifts the moment the incident resolves.
- `GET /v1/quality/health` and `GET /v1/quality/tier-advisor/{id}` both returned 200 with correct
  shapes (empty snapshots — expected, no completed week has rolled up yet for a number created
  seconds earlier; tier advisor correctly reported `tier_1k → tier_10k`, "not ready to grow" at
  zero recorded volume).
All fixture rows (business account, phone number, outbound message/outbox, guardian incidents,
quality events, the throwaway tenant-admin user + its membership) deleted afterward — confirmed
via a zero-row verification query.

`dotnet build wavio.slnx`: 0 errors. `dotnet test wavio.slnx`: 400/400 passed across all 6 test
projects (108 in WaIntel.Tests, up from ~70; 83 in WaGateway.Tests, up from ~68), including after
both live-fix commits.

**Deliberately out of scope**: block-rate telemetry's per-template correlation (spec asks to
"auto-flag offending template," but no template catalog cross-reference was built — `block_proxy_rate`
is computed per-number only, `IsBlockRateSpike` exists but nothing calls it to open a
`block_rate_spike` incident yet); `template_paused` incident type (exists in the DB CHECK
constraint, unused — that's wa-admin-svc's territory, issue #16). WaIngest's `MetaWebhookNormalizer`
was NOT touched — its `PreviousRating: "UNKNOWN"`/`PreviousTier: null` gap is correctly closed on
the WaIntel/Guardian side instead (see decision #4 above), exactly as its own doc comment
anticipated.
