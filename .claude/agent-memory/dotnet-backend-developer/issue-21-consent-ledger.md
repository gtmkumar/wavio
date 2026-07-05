---
name: issue-21-consent-ledger
description: Issue #21 Consent ledger (DPDP) — opt-in evidence, STOP listener, suppression enforcement, erasure/export, retention policies, and the platform-admin+X-Tenant-Id RLS write gap found live
metadata:
  type: project
---

Built the DPDP consent ledger (spec §4.10, issue #21) inside **wa-admin-svc**
(`WaAdmin.{Application,Infrastructure,WebApi}`), reusing `db/migrations/V012__consent.sql`'s
already-existing schema (`consent.opt_in_events`/`opt_out_events`/`erasure_requests`/
`retention_policies`) and giving the previously-unmapped `messaging.suppression_list` (V007) its
first EF entity, reader, and writer.

**Files**: `wavio.SharedDataModel/Entities/Consent/*` + `Persistence/Configurations/Consent/*`
(OptInEvent, OptOutEvent, ErasureRequest, RetentionPolicy), plus
`Entities/Messaging/SuppressionListEntry.cs` + its configuration — all wired into the shared
`WavioDbContext` and exposed through `WaAdmin`'s own `IWaAdminDbContext`/`WaAdminDbContext` adapter
(same "entities live in SharedDataModel, each service gets a thin DbSet-subset adapter" pattern as
#19/#20). `WaAdmin.Application/{Consent,ErasureRequests,RetentionPolicies}` follow the
feature-folder CQRS shape (no MediatR, manual guard-clause validation — the CQRS
ValidationBehavior pipeline is still dead code, see
[[cqrs-validation-pipeline-is-dead-code]]). Pure logic: `OptOutKeywordMatcher` (EN/HI/romanized
STOP vocabulary, token-based matching) and `ConsentStateResolver` (derives current per-purpose
consent from the append-only ledgers), both fully unit-tested with no I/O.
`WaAdmin.Infrastructure` gained `StopKeywordConsumerService` (consumes `wa.message.received.v1`,
mirrors `TemplateEventsConsumerBackgroundService`'s DLQ/retry shape, issue #16, NOT WaIntel's
simpler ack-and-log shape — a STOP keyword is compliance-bearing), its own
`WabaPhoneNumberTenantResolver` copy (issue #15 pattern), `ErasureRequestProcessorService`
(background worker, cross-tenant scan on the Admin connection then per-request app-connection with
`app.tenant_id` GUC set, mirrors `HealthSnapshotRollupService`), and `RetentionPolicySeeder`
(seeds the 5 platform-default rows, runs in EVERY environment unlike `IdentitySeeder`'s
dev-only gate — see decision #3 below). `WaAdmin.WebApi/Endpoints/Consent.cs`:
`POST/GET /v1/consent/opt-in|opt-out|{waId}`, `POST/GET /v1/consent/requests[/{id}]`,
`GET/PUT /v1/consent/retention-policies`. Also touched outside WaAdmin: `WaGateway.Application`'s
`SendMessageHandler`/`IWaGatewayDbContext` (suppression pre-dispatch gate — see decision #1),
`WaPlatform.Contracts.MessageReceivedV1` + `WaIngest.Application`'s normalizer (additive `Text`
field — see decision #2), `core.Infrastructure/Seeders/IdentitySeeder.cs` (6 new `consent.*`
permission codes + role grants). `tests/WaAdmin.Tests/{Consent,ErasureRequests,RetentionPolicies}`
(62 new tests, 97→159) + 3 new `tests/WaGateway.Tests` suppression cases (83→86) + 1 new
`WaIngest.Tests` normalizer case (37→38).

## Key design decisions

1. **Suppression enforcement lives in `SendMessageHandler` (accept-time, synchronous), NOT the
   async `OutboxDispatcherService` (Guardian's placement, issue #20).** Found by reading
   `MessageSendFailedV1`'s own doc comment before writing anything: it already anticipated a
   `SUPPRESSED` pre-dispatch reason code sitting alongside `WINDOW_CLOSED` — and `WINDOW_CLOSED`
   is checked synchronously in `SendMessageHandler`, not the dispatcher. Mirrored that exactly:
   `isSuppressed` is checked BEFORE the window policy (deny-wins — suppression beats an open
   window), only gates `templateCategory == "marketing"`, and rejects with `ErrorCode =
   "SUPPRESSED"` the same way `WINDOW_CLOSED` does. A future editor reaching for
   `GuardianThrottleGate`'s dispatcher-side pattern here would be solving the wrong layer.
2. **`MessageReceivedV1` never carried message body text — extended it additively with a nullable
   `Text` field**, populated by `WaIngest.Application`'s normalizer only when `type == "text"`
   (Meta's `text.body`). No prior consumer of this event (WaIntel's window manager) ever needed
   it. Same "additive field, cross-service touch flagged so it isn't mistaken for scope creep" move
   as #19's `MessageStatusV1` PMP extension.
3. **`RetentionPolicySeeder` runs in EVERY environment, not Development-only like
   `IdentitySeeder`.** These are baseline reference-data rows (platform-default retention days),
   not sensitive bootstrap credentials — every tenant needs a fallback default to exist. Raw
   ADO.NET on the Admin connection (`ON CONFLICT (tenant_id, data_class) DO NOTHING`), called
   unconditionally from `WaAdmin.WebApi`'s `Program.cs` whenever `ConnectionStrings:Admin` is
   configured.
4. **Retention-day values are the spec's own numbers plus two documented judgment calls**:
   `message_content`=365 (spec §4.10, 12mo), `metadata`/`cost_ledger`=2920 (spec §4.10, 8y tax),
   `consent_evidence`=2920 (judgment call — same tax/audit bucket, spec doesn't give this one a
   number), `raw_webhook`=30 (NOT invented — taken from spec §6's own schema outline
   "ingest.raw_webhooks (30-day TTL)", found by re-reading the whole spec section rather than
   guessing 90 as the task brief itself had guessed).
5. **Suppression enforcement is deliberately MARKETING-ONLY, uniform regardless of
   `opt_out_events.scope`.** `messaging.suppression_list` (V007, schema frozen) has no scope
   column — spec §4.10's own wording is "immediate MARKETING suppression per (tenant, wa_id)". A
   `scope='all'` opt-out still produces exactly one suppression_list row; there is nowhere else in
   the frozen schema to record "block everything," and `SuppressionListEntry`'s doc comment flags
   this as a real Wave-1 gap, not a silent one.
6. **STOP-listener idempotency has no DB unique constraint to lean on** (`opt_out_events` has no
   unique index on `inbound_wamid`, V012). `RecordOptOutCommandHandler` does an app-level
   check-then-insert on `(tenant_id, inbound_wamid)` before writing, for `reason=stop_keyword`
   only. Verified live: republishing the identical `wa.message.received.v1` payload left the
   opt-out-event and suppression-list row counts unchanged at 1.
7. **Erasure blanks `payload` on `messaging.outbound_messages`/`inbound_messages` only** — every
   other column (ids, wamid, status, timestamps) survives so correlation/audit keeps working.
   `billing.message_costs` is never touched by the worker at all, because it has no `wa_id` column
   (V010) — "preserve the cost ledger" requires zero special-casing, it's simply a different
   schema this worker never writes to. Verified live (see below).
8. **Export scope is message METADATA, not content** (wamid/type/status/timestamps), plus consent
   events and cost-ledger rows, written to a local JSON file under `Consent:ExportDirectory`
   (default `./data/consent-exports`) with `export_ref` = the file path — documented as the
   pragmatic v1 shortcut a real deployment would replace with object storage + a signed URL.

## Bug found live during acceptance verification (not by unit tests)

**Platform-admin + `X-Tenant-Id` header cannot WRITE to any strict-RLS table via the normal
`app_user` connection**, even though `ICurrentUser.RequireTenantId()` (WaBilling's `Quotas.cs`
convention, correctly used everywhere in this feature) resolves the override tenant fine at the
Application layer. Root cause: `RlsConnectionInterceptor` sets `app.tenant_id` from
`ICurrentTenant.TenantId` — the raw JWT claim ONLY, no `X-Tenant-Id` awareness — and
`app.is_platform_admin()` (V001) is a Postgres ROLE-membership check (`pg_has_role(current_user,
'platform_admin', 'member')`), not a GUC read; `app_user` is never granted that DB role
("`app_user` must never be granted `platform_admin`," per the migration's own comment). So a
platform-admin token writing under an `X-Tenant-Id` override hits `INSERT ... violates row-level
security policy` — the DB session genuinely has no tenant context AND doesn't pass the
platform-admin OR-clause either. This is a deeper version of the gap
[[issue-20-quality-guardian]] already flagged for reads; here it blocks writes too. Worked around
exactly like #20 did: created a throwaway tenant-scoped `tenant_admin` fixture user (copied the
seeded admin's password hash) for every live write in this feature's verification. **Not fixed
in this issue** — a real fix would need `RlsConnectionInterceptor` to also honor
`HttpContext.Items["tenant_id_override"]`, which is a cross-cutting `wavio.SharedDataModel`/
`wavio.Utilities` change outside this issue's scope; flagging here so the next issue that needs a
platform-admin write path doesn't rediscover it from scratch.

## Verified live (not just unit tests)

Booted `core.WebApi` (5056), `WaAdmin.WebApi` (5103), `WaGateway.WebApi` (5101), and
`tools/MetaGraphSendApiStub` (5299) standalone against the real `waplatform` Postgres (V001–V012)
and the real `wavio-rabbitmq`. `RetentionPolicySeeder` inserted 5/5 platform defaults on its own
first boot. Logged in as the seeded platform_admin, completed OTP step-up (note: `/otp/verify`'s
own returned token does NOT carry the step-up claim — only `/auth/step-up/verify`'s does; the two
otp-verification endpoints look interchangeable but aren't), created the fixture tenant-admin user
per the bug above, seeded a fixture `waba.business_accounts`/`phone_numbers` row, then:
- `POST /v1/consent/opt-in` (marketing) → `GET /v1/consent/{waId}` correctly reported
  `optedIn: true` for marketing, false for transactional/service.
- Published a `wa.message.received.v1` event with `text: "STOP"` via the RabbitMQ HTTP API →
  `opt_out_events` row (`stop_keyword`/`stop`/`en`) and `suppression_list` row appeared.
- Published a second event with `text: "बंद"` (different wa_id) → `opt_out_events` row
  (`stop_keyword`/`बंद`/`hi`) and its own `suppression_list` row appeared.
- Republished the FIRST event byte-for-byte → row counts stayed at 1/1 (idempotent redelivery).
- `POST /api/v1/messages` (marketing category) to the suppressed wa_id →
  `422 SUPPRESSED`, zero `outbound_outbox` rows written.
- `POST /api/v1/messages` (utility category) to the SAME suppressed wa_id → `202 accepted`,
  dispatched by the real `OutboxDispatcherService` against the Graph stub, got a real `wamid`.
- Inserted a `billing.message_costs` row against that `wamid`; `POST /v1/consent/requests`
  (erasure, after step-up) → `ErasureRequestProcessorService`'s next tick (30s poll) completed it:
  both `outbound_messages.payload` rows for that wa_id became `{}`, `billing.message_costs`
  (amount/category/currency) was completely untouched, `consent.opt_in_events`/`opt_out_events`
  rows for that wa_id were untouched, and an audit-log row (`consent.erasure_request_processed`,
  no wa_id in `new_values`) was written.
- `GET /v1/consent/requests/{id}` returned `status: completed` with `contentErasedAt`/`completedAt`
  set.
- `PUT /v1/consent/retention-policies` (message_content, 180 days) created a NEW tenant-override
  row; the platform-default (`tenant_id IS NULL`) row for the same data class stayed at 365 —
  confirmed the upsert never touches the default row.
All fixture rows (business account, phone number, throwaway tenant-admin user + membership, both
wa_ids' opt-in/opt-out/suppression/outbound-message/message-cost rows, the erasure request, the
one tenant retention-policy override) deleted afterward — confirmed via a zero-row verification
query. The seeded 'Default Tenant' and the 5 platform-default retention rows were left in place.

`dotnet build wavio.slnx`: 0 errors, 0 warnings on the final run. `dotnet test wavio.slnx`:
466/466 passed across all 6 test projects (159 in WaAdmin.Tests, up from 97; 86 in
WaGateway.Tests, up from 83; 38 in WaIngest.Tests, up from 37).

**Deliberately out of scope**: a retention-policy ENFORCEMENT sweep (actually deleting/archiving
data once it ages past `retention_days`) — needs a product decision on deletion semantics vs. this
issue's own erasure workflow, per the task brief's own framing; `RetentionPolicy`'s doc comment
flags this. `system.audit_log`'s own retention isn't governed by this ledger. The export artifact
is a local file, not object storage (decision #8 above) — a real deployment needs to swap that.
