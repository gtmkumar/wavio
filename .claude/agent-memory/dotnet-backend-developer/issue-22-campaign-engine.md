---
name: issue-22-campaign-engine
description: Issue #22 Campaign engine (broadcast, tier-aware chunking) in wa-gateway-svc — reuse of the send accept path, tier headroom design, the EF FK-ordering bug and the wrong-route-prefix bug found live
metadata:
  type: project
---

Built the campaign engine (spec §4.2/§7.1, issue #22) inside **wa-gateway-svc**
(`WaGateway.{Application,Infrastructure,WebApi}`), on top of `db/migrations/V013__campaigns.sql`
(`messaging.campaigns`/`campaign_recipients`, already applied before this issue started).

**Files**: `wavio.SharedDataModel/Entities/Messaging/{Campaign,CampaignRecipient}.cs` + matching
`Persistence/Configurations/Messaging/*`, both DbSets added to `WavioDbContext`.
`WaGateway.Application/Campaigns/{Commands/{CreateCampaign,LaunchCampaign,CancelCampaign},
Queries/{GetCampaign,ListCampaigns},Dtos,Logic}` (CQRS feature-folder shape, no MediatR) — pure
logic in `CampaignTierRules` (tier-code→numeric-limit + headroom + Guardian-halving, mirrors
WaIntel's `TierRules` as a local copy, not a shared reference — same duplication convention as
`GuardianThrottleRules`) and `CampaignRecipientStatusRules` (forward-transition guard, completion
check). `IWaGatewayDbContext` gained `Campaigns`/`CampaignRecipients` (owned, read-write) and
`Templates`/`TemplateVersions` (cross-schema read-only, same "same-DB different-schema reads are
an established convention for background scanners" precedent as issue #20's `HealthSnapshotRollupService`).
`WaGateway.Infrastructure`: `HttpBillingEstimatorClient` (new `IBillingEstimatorClient`, HTTP hop
to wa-billing-svc's estimator, mirrors `HttpWindowStateClient`'s caller-token-forwarding pattern
but NOT its fail-closed default — see decision 1), `CampaignChunkerService` (new BackgroundService,
mirrors `OutboxDispatcherService`/`ErasureRequestProcessorService`'s cross-tenant-discovery-then-
scoped-work shape), `CampaignStatusConsumerService` (new, consumes `wa.message.status.v1`, mirrors
WaBilling's `MessageStatusConsumerService`), a new local `ITenantResolver` +
`WabaPhoneNumberTenantResolver` copy (same per-service-copy convention as issues #19/#20/#21).
`WaGateway.WebApi/Endpoints/Campaigns.cs`: `POST /v1/campaigns`, `POST /v1/campaigns/{id}/launch`,
`POST /v1/campaigns/{id}/cancel`, `GET /v1/campaigns/{id}`, `GET /v1/campaigns`. Also touched:
`core.Infrastructure/Seeders/IdentitySeeder.cs` (5 new `campaigns.*` permission codes — `launch`
is High risk, so it requires step-up; `list`/`read`/`create` granted to staff, `launch`/`cancel`
tenant_admin-only). `tests/WaGateway.Tests/Campaigns/*` (34 new tests: `CampaignTierRulesTests`,
`CampaignRecipientStatusRulesTests`, and handler tests for all four commands/queries via the
existing `InMemoryWaGatewayDbContext` idiom — 146 total WaGateway tests, up from ~112).

## Key design decisions

1. **Fan-out reuses `SendMessageCommand`/`SendMessageHandler` verbatim, not a parallel send path.**
   `CampaignChunkerService.DispatchOneRecipientAsync` calls `IDispatcher.SendAsync(new
   SendMessageCommand(...))` — the exact call `POST /v1/messages` makes — so suppression
   (re-checked live, deny-wins), the window policy, the transactional outbox, the token bucket,
   and the Guardian throttle all run through the SAME code paths for a campaign recipient as an
   ad hoc send. The idempotency key is deterministic (`campaign:{campaignId}:{recipientId}`), which
   is also what makes a claim-race between two chunker ticks/instances harmless (see decision 3).
2. **Campaign category is resolved from the pinned template, not caller-declared.** Issue #14's
   `TemplatePayload.Category` was caller-declared (a documented Wave 1 scope cut). A campaign
   always has a real `templates.templates` row to look up via `template_versions.template_id`, so
   `CreateCampaignCommandHandler` uses `Template.Category` as the authoritative category for
   suppression/headroom/Guardian decisions — closing that gap for the campaign path specifically.
3. **Tier headroom is a NEW pragmatic-v1 DB counter, deliberately separate from the existing
   in-memory `MessagingTierGate`** (issue #14, which uses one global config default for every
   phone number, not the number's real tier). `CampaignTierRules.ComputeChunkSize` combines: (a)
   `waba.phone_numbers.messaging_tier`'s raw Meta code → numeric daily limit (unrecognized/null →
   the most conservative known tier, 250/day, fail-closed); (b) "consumed" = distinct
   marketing-template recipients in the trailing 24h, computed by loading a narrow
   (tenant, phone number, type, time-window)-filtered projection of `outbound_messages` and
   filtering/deduping category in C# — the same "deserialize jsonb in memory" idiom as
   `OutboxDispatcherService.IsMarketingTemplate`, since there's no jsonb-indexed category column;
   (c) Guardian YELLOW halves the CHUNK SIZE (how fast this tick consumes the SAME headroom), not
   the tier ceiling itself — a documented interpretation, not a spec-literal one. RED
   (`marketing_frozen`) skips the campaign entirely for the tick, recipients stay pending.
4. **Template pause/disable is wired into both launch and the chunker**, per V009's own migration
   comment ("Guardian ... freeze campaigns using paused template") — read closely because the task
   brief didn't call it out explicitly. PAUSED holds chunking for the tick only (resumes
   automatically, same "throttling is not a failure" treatment as Guardian's freeze). DISABLED is
   terminal in the template state machine, so `LaunchCampaignCommandHandler` rejects launching
   against one, and the chunker fails an already-running campaign outright (cancelling its
   remaining pending recipients) if the template gets disabled mid-flight.
5. **Completion is literal**: "no pending/sent recipients remain" (V013's own status-transition
   comment), meaning a recipient parked at `sent` with no further delivery/read webhook EVER
   arriving (Meta doesn't guarantee read receipts) blocks completion forever — a documented Wave 1
   gap in `CampaignRecipientStatusRules.IsCampaignComplete`'s doc comment, not silently accepted.
6. **`CancelCampaignCommandHandler` does NOT use `ExecuteUpdateAsync`** for its bulk
   pending→cancelled recipient transition, unlike the chunker/consumer — the EF Core InMemory
   provider this handler's own unit tests run against throws `InvalidOperationException` on
   `ExecuteUpdateAsync` (confirmed by running the tests: `CancelCampaignCommandHandlerTests` failed
   until this was found). Uses tracked-entity load+mutate+one-`SaveChangesAsync` instead, same
   convention as `SendMessageHandler`. This leaves a narrow, documented Wave 1 race with a
   concurrently-ticking chunker (last-write-wins on a recipient's status, no concurrency token on
   `campaign_recipients`) — accepted as low-severity (a human-triggered, low-frequency action; the
   worst outcome is a stale counter/status, never a duplicate send) and flagged in the handler's
   own comment, same class of tradeoff as `MessagingTierGate`'s per-instance limitation.

## Two bugs found LIVE during acceptance verification (not by unit tests)

**Bug 1 — EF SaveChanges insert-ordering violated `campaign_recipients_campaign_id_fkey`.**
`CreateCampaignCommandHandler` adds one `Campaign` + N `CampaignRecipient` rows and calls
`SaveChangesAsync` once. Neither entity's EF configuration declared a modeled relationship (both
are plain POCOs, same "no navigation properties" convention as everywhere else in this codebase) —
without one, EF's SaveChanges dependency graph has no way to know a new `CampaignRecipient` must
be inserted after its parent `Campaign` in the same batch, and (compounded by
`AuditSaveChangesInterceptor`'s extra `audit_logs` inserts riding along in the same unit of work)
the recipient inserts executed first, 23503-ing on the very first live create attempt. This is the
IDENTICAL bug class `TemplateVersionConfiguration.cs` already documents and fixes for
`Template`↔`TemplateVersion` (issue #16) — I hadn't re-read that file closely enough before
writing my own config. Fixed the same way: `CampaignRecipientConfiguration` now has
`builder.HasOne<Campaign>().WithMany().HasForeignKey(e => e.CampaignId).OnDelete(DeleteBehavior.Cascade)`
with no CLR navigation property added. **Rule of thumb for any future 1-parent/N-child entity pair
inserted together in one `SaveChangesAsync`**: grep for how the last one did it
(`TemplateVersionConfiguration.cs`) before assuming "no navigation property" also means "no
`HasOne`/`HasForeignKey` needed" — those are two different things, and only the InMemory-based
unit tests (which don't enforce FK constraints at all) will stay silent about the gap.

**Bug 2 — `HttpBillingEstimatorClient` called the wrong route prefix.** WaGateway's own endpoints
are mapped at `/api/v1/...` (`Messages.cs`, `Campaigns.cs`), so I assumed WaBilling's `Costs`
endpoint followed the same convention and called `/api/v1/costs/estimate` — it 404's (route not
matched at all, not an auth 401) because `WaBilling.WebApi/Endpoints/Costs.cs` maps its
`RoutePrefix` at `/v1/costs`, no `/api` segment. Each service's `IEndpointGroup.RoutePrefix` is
independent — there is no cross-service convention to assume. Fixed by correcting the hardcoded
path to `/v1/costs/estimate`; the client's `EstimateAsync` doc comment now flags this so a future
editor doesn't reintroduce the same wrong assumption for a different cross-service HTTP client.
Silent failure mode worth noting: because `IBillingEstimatorClient`'s contract treats "unreachable
or non-success" as advisory-only (null → leave `projected_cost` null), this bug did NOT throw or
fail campaign creation — it just silently produced `projectedCost: null` for every campaign until
caught by explicitly checking the gateway's own warning log during live verification. A future
similar advisory-only integration should have its "unreachable" log line checked during
verification, not just its happy path.

## Verified live (not just unit tests)

Booted `core.WebApi` (5056), `WaGateway.WebApi` (5101), `WaBilling.WebApi` (5104), and
`tools/MetaGraphSendApiStub` (5299) standalone against the real `waplatform` Postgres (V001–V013)
and the real `wavio-rabbitmq`. Created a throwaway tenant-scoped `tenant_admin` fixture user (per
[[platform-admin-write-rls-gap]] — campaigns.* writes are strict-RLS) plus a fixture
`waba.business_accounts`/`phone_numbers` (messaging_tier=`TIER_250`) row and an APPROVED
`templates.templates`/`template_versions` row and a `billing.rate_cards`/`rate_card_entries` row
(marketing/IN, ₹0.7876/msg — WaBilling had no rate card loaded at all before this, so the
estimator's own "Found: false" path was exercised first, then a real priced entry).

Simulated a small tier headroom (250/day) by pre-seeding 248 fake same-day marketing-template
`outbound_messages` rows for the fixture phone number, then:
- `POST /v1/campaigns` with a 5-member audience (1 pre-suppressed via a `messaging.suppression_list`
  row) → `audienceCount:5, suppressedCount:1, projectedCost:3.1504 (INR)` — 0.7876 × 4 billable
  recipients, confirming both the up-front suppression marking and the estimator aggregation.
- OTP step-up (`campaigns.launch` is High risk) → launch → `status: running`.
- Chunker's next tick (15s default): claimed exactly 2 of the 4 pending recipients (headroom =
  250 tier limit − 248 already-consumed = 2) — confirmed by both `campaign_recipients` status
  (`sent`) and the Graph stub's own request log showing exactly 2 `POST .../messages` calls; the
  other 2 recipients stayed `pending`, zero Graph calls for them.
- Resume: aged the 248 fixture rows' `accepted_at` to 30h ago (simulating a day passing) → next
  tick dispatched BOTH remaining pending recipients (headroom now 248) — proving the multi-day
  resume is real, not just theoretical.
- Published a synthetic `wa.message.status.v1` (`status: delivered`) via the RabbitMQ HTTP API for
  one dispatched recipient's `wamid` → `campaign_recipients.status` → `delivered`,
  `campaigns.delivered_count` → 1, confirmed via `GET /v1/campaigns/{id}`.
- Opened a `quality.guardian_incidents` row (`quality_red`/`marketing_frozen`) for the phone
  number, added 2 more pending recipients directly → the very next tick logged "Guardian has
  frozen marketing sends... skipping this tick" and both recipients stayed `pending` with zero new
  Graph stub calls.
All fixture rows (campaign + recipients, guardian incident, outbound messages + outbox entries,
suppression-list row, rate card + entry, template + version, phone number, business account, the
throwaway tenant-admin user + membership) deleted afterward — confirmed via a zero-row
verification query across every touched table. The seeded 'Default Tenant' was left in place.

`dotnet build wavio.slnx`: 0 errors, 0 new warnings (fixed 2 introduced CA1305/CA1822 warnings
before commit). `dotnet test wavio.slnx`: 526/526 passed across all 6 test projects (146 in
WaGateway.Tests, up from ~112), including after both live-fix commits.

**Deliberately out of scope**: block-rate/health-report correlation with campaigns (issue #20's
own scope, untouched). Actual template-variable substitution (campaign/recipient `Params` are
opaque, caller-supplied Meta-component JSON, same convention as issue #14's `ComponentsJson` — no
templating engine was built). Multi-instance chunker leases (no `locked_by`/`locked_at` column on
`campaign_recipients` — schema frozen at V013; claim races are tolerated via idempotent dispatch +
fenced status transitions, not prevented via a lease, documented in `CampaignChunkerService`'s
class doc comment). A dedicated campaign-scheduler background service was NOT built — the
existing chunker's discovery query also promotes due `scheduled` campaigns to `running` in the
same tick, reusing one loop instead of standing up a second one.
