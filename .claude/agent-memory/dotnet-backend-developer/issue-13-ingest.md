---
name: issue-13-ingest
description: What was built for issue #13 (wa-ingest-svc webhook receiver), key design decisions, and a live-verification-found security bug + fix
metadata:
  type: project
---

# Issue #13 — wa-ingest-svc webhook receiver (2026-07-03)

Branch `feature/13-ingest-webhooks` off `feature/17-wave1-migrations`. PR stacked
the same way as #10/#17 (base = `feature/17-wave1-migrations`, queued behind it).

## What was built
- **WaIngest.WebApi/Endpoints/Webhooks.cs**: `GET /api/v1/webhooks/meta` (Meta
  subscription handshake), `POST /api/v1/webhooks/meta` (webhook delivery),
  `POST /api/v1/webhooks/meta/replay` (degraded-mode recovery, gated on
  `permission:ingest.webhooks.replay` — see the security-review round below).
- **WaIngest.Application/Security/MetaWebhookSignatureVerifier.cs**: pure,
  constant-time HMAC-SHA256 verify over raw bytes (never re-serialized JSON).
- **WaIngest.Application/Ingestion/Normalization/MetaWebhookNormalizer.cs**: pure
  function, Meta webhook JSON → the 9 typed `WaPlatform.Contracts` events. Added
  two missing contracts: `TierChangedV1` and `TemplateCategoryChangedV1`
  (`WaPlatform.Contracts/IntegrationEvents/V1/AccountEvents.cs` /
  `ChannelEvents.cs`) — the original contract set only had 7 of the 9 the issue's
  task list named.
- **WaIngest.Application/Ingestion/WebhookProcessor.cs**: dedupe-check → publish
  → dedupe-insert, in that order — a dedupe row is only recorded AFTER a
  publish actually succeeds, so a failed attempt (bus down) is indistinguishable
  from "never tried" and safe to replay later; recording it before publish
  would permanently block the real send once the bus recovers. Orchestrates
  one raw_webhooks row; used by both the live background worker and replay.
- **WaIngest.Infrastructure/Messaging**: `RabbitMqConnectionManager` (lazy
  singleton connection, 5s connect timeout, no internal retry-loop — a broker
  outage must surface as a fast exception) + `RabbitMqEventBusPublisher`
  (exchange `wavio.events`, topic, durable; routing key = event name). RabbitMQ.Client
  7.1.2 — first bus producer in the repo, no prior convention to match.
  Consumers (#14/#15/#16/#19+) bind their own queues to this exchange.
  ConnectionStrings:RabbitMq was already wired end-to-end in AppHost.cs (issue
  #8 scaffold) — nothing needed there.
- **WaIngest.Infrastructure/BackgroundWork/WebhookIngestBackgroundService.cs**: drains
  an in-process bounded `Channel` (`WaIngest.Application/Ingestion/WebhookIngestBuffer.cs`
  — named "Buffer" not "Queue" to dodge CA1711) and runs a periodic sweep
  (every 30s, not just once at startup — see security-review round below) for
  stale 'received' rows.
- **ingest.raw_webhooks / ingest.webhook_dedupe EF mapping**: added to
  `wavio.SharedDataModel` (`Entities/Ingest`, `Persistence/Configurations/Ingest`,
  registered on `WavioDbContext`) — these tables existed in the DB since V003
  (#10) but had no EF entities yet. `IWaIngestDbContext` +
  `WaIngestDbContext` adapter follow the exact `ICoreDbContext`/`CoreDbContext`
  pattern from core.Application/core.Infrastructure.
- **tests/WaIngest.Tests** (new xUnit project, registered in `wavio.slnx`):
  36 tests — signature verify, normalizer (all 9 event kinds + flow-reply +
  payment-status detection + unrecognized-field skip + a dedupe-suffix-bounding
  case), WebhookProcessor (dedupe-once, bus-down failure, replay-after-recovery,
  missing-row, signature-invalid-never-published), WebhookIngestBuffer
  (drop-on-full never blocks), RabbitMqConnectionManager (fail-closed outside
  Development), and `Webhooks.ReceiveWebhook` exercised directly against a
  fabricated `DefaultHttpContext` (no test server needed — see
  `WaIngest.WebApi.csproj`'s `InternalsVisibleTo` for `WaIngest.Tests`, added
  during the security-review round below).
  `Microsoft.EntityFrameworkCore.InMemory` backs a hand-rolled
  `InMemoryWaIngestDbContext` since `DbSet<T>` can't be mock-faked directly.
  `<NoWarn>CA1707</NoWarn>` in the test csproj for xUnit's
  `Method_Scenario_Expected` naming (repo's `AnalysisLevel=latest-recommended`
  otherwise flags every test method name).

## Scope decisions (read before touching this area again)
- **Tenant resolution is NOT implemented.** `waba.phone_numbers` is empty
  (issue #6 onboarding doesn't exist yet) AND is RLS-scoped — even once rows
  exist, an unauthenticated webhook receiver can't look them up without
  already knowing the tenant (chicken-and-egg). Every published event carries
  `TenantId = Guid.Empty` plus Meta's raw `phone_number_id`/`waba_id` strings
  (already fields on every contract). Real resolution needs a narrow,
  audited platform_admin-scoped lookup — future work once WaAdmin
  provisioning lands.
- **messaging.inbound_messages / message_statuses are NOT written by this
  service**, despite the database-architect's forward-looking handoff note
  suggesting "(#13)" should set the tenant GUC and write them. Both columns
  are `NOT NULL` FKs to `tenancy.tenants`/`waba.phone_numbers` — with tenant
  resolution unavailable (previous point), these inserts are impossible to
  satisfy in Wave 1, not just deferred by choice. wa-ingest-svc's job per the
  issue's actual task list is verify → persist → dedupe → normalize → publish;
  writing normalized domain rows into `messaging.*` is a consumer's job (#14
  most likely, since it owns that schema), not the publisher's.
- **billing.message_costs** — deferred to Wave 2 (#19) per explicit orchestrator
  ruling. `wa.message.status.v1` carries the full raw `pricing`/`conversation`
  objects verbatim for backfill; `raw_webhooks` keeps everything regardless.
- **Payment-status detection is a guess.** No real WhatsApp Payments payload
  exists yet (Wave 3, #26) — the normalizer looks for a `payment` object
  nested in a status entry; documented as needing hardening once #26 lands
  with a real payload to test against.

## Live-verification-found bug (fixed)
Replaying by time window (the degraded-mode recovery path) would resurrect and
actually publish a webhook that had **failed signature verification** — the
query only checked `processing_status IN ('received','failed')`, not
`signature_valid`. Reproduced against real RabbitMQ (a forged payload landed
on the bus), then fixed at two layers: the replay query now requires
`SignatureValid == true` unconditionally, and `WebhookProcessor.ProcessAsync`
independently refuses to process any row where that isn't true. This is
exactly the kind of thing that only shows up with a live run, not unit tests
written before the fact — the regression test added afterward
(`ProcessAsync_SignatureWasInvalid_NeverPublishesEvenIfExplicitlyTargeted`)
would not have existed without live verification catching it first.

## Security-review round (PR #41, same day) — 5 more findings, all fixed
A dedicated security-code-reviewer pass on the PR found real issues beyond
what my own live verification had caught. All fixed in commit `c201757`,
36/36 tests green, 135-warning baseline unchanged:

- **B1 (blocking)**: `ReceiveWebhook` used to JSON-parse and persist the
  **real request body** before checking the signature — any unauthenticated
  caller could write up to 1MB of arbitrary bytes into shared Postgres per
  request. Fixed by moving signature verification to the very first thing
  done with the body (right after the bounded read): on failure, only a
  small fixed-shape stub (`{"note":"signature_invalid","bytes":N}`) is
  persisted, never the caller's bytes. Verified live: a 999KB unsigned
  valid-JSON POST → 401 + a 46-byte stub row; the attacker's marker string
  never appeared anywhere in the DB.
- **S1**: the size guard was Content-Length-based only, which a chunked
  request (no Content-Length header) bypasses entirely. Fixed with a bounded
  read loop (`TryReadBoundedBodyAsync`) that aborts the instant cumulative
  bytes read would exceed the limit, regardless of any declared length.
  Note: this curl/environment always resolved a real Content-Length even
  when piping through a FIFO, so the "no Content-Length at all" case is
  proven by a unit test constructing an `HttpContext` with
  `ContentLength = null` directly, not by a live curl repro.
- **S2**: `RabbitMqConnectionManager` fell back to
  `amqp://guest:guest@localhost:5672` in every environment, not just
  Development. Fixed at two layers — `Program.cs` fails eagerly at boot
  (live-verified: `ASPNETCORE_ENVIRONMENT=Production` + unset
  `ConnectionStrings:RabbitMq` → immediate `InvalidOperationException`,
  process exits) and the constructor independently refuses the same
  fallback for any other composition root.
- **S3**: `POST .../replay` was `RequireAuthorization()` only — any tenant
  JWT could invoke an internal recovery tool. Now gated on
  `permission:ingest.webhooks.replay`, a code deliberately absent from
  core's seeded permission catalog (`core.Infrastructure/Seeders/
  IdentitySeeder.cs`) so no tenant-scoped role can hold it — only a
  `platform_admin` JWT passes, via `PermissionHandler`'s existing
  `user_type == platform_admin` bypass (Gate 2). If a future non-platform-
  admin operator role needs this, it needs a `PermissionDefs` entry +
  grant added in core first — that's a cross-service change I didn't make.
- **S4**: `WebhookIngestBuffer` used `BoundedChannelFullMode.Wait` — during
  a sustained bus outage, once the 10k-capacity buffer filled, `EnqueueAsync`
  would **block the HTTP ack path itself**, breaking the <1s NFR and very
  plausibly triggering Meta's own retry amplification on top of an already-
  degraded bus. Switched to `DropWrite` + `TryWrite` (never blocks; the row
  is already durable in Postgres regardless of whether the reference makes
  it into the buffer) and changed `WebhookIngestBackgroundService`'s
  stale-row recovery from startup-only to a `PeriodicTimer`-driven sweep
  (every 30s). Live-verified end-to-end: with RabbitMQ pointed at an
  unreachable port, a 15,000-request burst (`ab -n 15000 -c 100`) kept ack
  p50/p99 in the tens-of-ms range throughout (proving the buffer never
  blocked), immediately left ~9,200 rows sitting at `processing_status =
  'received'` (proof the buffer actually dropped ~5,000 of the 15,000 that
  never fit), and then converged to zero 'received' rows in **exact
  1,000-row steps roughly 30 seconds apart** — the unmistakable signature of
  the sweep's batch limit — with 100% of the 15,000 rows eventually reaching
  a terminal state and zero permanent loss.
- **N1**: the dedupe `event_type` (`ingest.webhook_dedupe.event_type`,
  varchar(50)) embeds Meta's raw status string; an unexpectedly long value
  would fail the INSERT **after** a successful publish (per the
  dedupe-after-publish ordering above), which would incorrectly leave the
  row `'failed'` and cause a replay to publish a real duplicate. Fixed by
  bounding just the dedupe-key suffix to 20 chars — the full status value is
  untouched on the actual published event.

Net lesson for next time: build the dedupe-after-publish ordering and the
signature-before-anything ordering as the FIRST thing reviewed, not
discovered after the fact — both bugs found in this issue (the replay
double-publish and this round's B1) were ordering mistakes in code that
looked correct read top-to-bottom but processed things in the wrong
sequence relative to a trust boundary.
