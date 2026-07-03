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
  `POST /api/v1/webhooks/meta/replay` (degraded-mode recovery, `RequireAuthorization()`).
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
  — named "Buffer" not "Queue" to dodge CA1711) and recovers stale 'received'
  rows on startup (crash between DB insert and enqueue).
- **ingest.raw_webhooks / ingest.webhook_dedupe EF mapping**: added to
  `wavio.SharedDataModel` (`Entities/Ingest`, `Persistence/Configurations/Ingest`,
  registered on `WavioDbContext`) — these tables existed in the DB since V003
  (#10) but had no EF entities yet. `IWaIngestDbContext` +
  `WaIngestDbContext` adapter follow the exact `ICoreDbContext`/`CoreDbContext`
  pattern from core.Application/core.Infrastructure.
- **tests/WaIngest.Tests** (new xUnit project, registered in `wavio.slnx`):
  24 tests — signature verify (valid/invalid/missing/malformed/tampered),
  normalizer (all 9 event kinds + flow-reply + payment-status detection +
  unrecognized-field skip), WebhookProcessor (dedupe-once, bus-down failure,
  replay-after-recovery, missing-row, signature-invalid-never-published).
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
