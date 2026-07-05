---
name: 2026-07-03-pr41-ingest-webhooks
description: Security audit of PR #41 (wa-ingest-svc webhook receiver, issue #13) — re-verified after fix commits c201757+735ba8f, now APPROVE (all 6 findings closed, 36/36 tests)
metadata:
  type: project
---

# PR #41 security audit (2026-07-03) — wa-ingest-svc webhook receiver

FINAL: APPROVE as of commit 735ba8f. Round 1 was REQUEST CHANGES (one Blocking); implementer fixed all six findings in c201757, verified 2026-07-03. 36/36 tests pass in isolated worktree, zero warnings from WaIngest projects.

## Round-2 re-verification (fix commits c201757 + 735ba8f)
- **B1 (Blocking) CLOSED**: signature now verified before the body is touched; invalid → 401 + fixed-shape stub row (`{note:"signature_invalid",bytes:N}`), attacker body never persisted/parsed. Unit test `ReceiveWebhook_InvalidSignature_PersistsOnlyAFixedStubNeverTheRealBody` asserts the attacker marker never reaches `command.Payload`. reject-before-deserialize now literally true.
- **S1 CLOSED**: `TryReadBoundedBodyAsync` bounds the read during copy (81920-byte chunks, aborts when cumulative > 1MB); tested with ContentLength=null (chunked-bypass case).
- **S2 CLOSED**: fails closed outside Development at two layers — eager check in Program.cs + `RabbitMqConnectionManager` ctor (now takes IHostEnvironment). Tested Production-throws / Development-fallback / configured-never-throws.
- **S3 CLOSED (acceptable Wave-1 floor)**: replay gated on `permission:ingest.webhooks.replay`. Verified against actual `PermissionHandler`: default-deny; only `user_type==platform_admin` bypasses Gate 2; the code is absent from the seeded catalog so no tenant role can hold it. Sound because RBAC is default-deny AND merely seeding the code later grants nothing without an explicit role assignment — the extension path is documented at the gate. Worst-case careless future grant is bounded (replay only republishes signature-valid, already-persisted rows; cannot forge events; dedupe ≈ idempotent).
- **S4 CLOSED**: buffer now `DropWrite` + `TryWrite` (ack never blocks) and a 30s `PeriodicTimer` sweep replaces startup-only recovery. Tested full-buffer never blocks/throws; sweep recovers dropped rows.
- **N1 CLOSED**: dedupe event_type suffix bounded to 20 chars (`BoundDedupeSuffix`); full status still on the published event.

Only forward-looking Nit (not blocking): when a future wave adds `ingest.webhooks.replay` to the seed catalog, keep it ungranted / document the cross-tenant event-injection risk in the seeder so a role-builder doesn't bundle it unknowingly.

## Round 1 findings (all now resolved — kept for history)
Verdict was REQUEST CHANGES (one Blocking finding). All 24 unit tests passed.

## Blocking
- **Pre-auth durable-write DoS**: `Webhooks.cs` `ReceiveWebhook` persists signature-INVALID payloads (up to 1MB each) into `ingest.raw_webhooks` (30-day retention) before returning 401 — "kept for forensics" by design, but gives any unauthenticated internet caller a durable write primitive into the shared Postgres. No rate limiting anywhere in the host.

## Should-fix (carried forward if not fixed in this PR)
- Body copied fully into MemoryStream before the 1MB check — chunked requests consume up to Kestrel default 30MB transient memory; should set per-request `IHttpMaxRequestBodySizeFeature.MaxRequestBodySize`.
- `RabbitMqConnectionManager` falls back to `amqp://guest:guest@localhost:5672` in ALL environments (contrasts with the fail-closed Meta-secret posture in Program.cs).
- Replay endpoint is `RequireAuthorization()` only — any authenticated tenant JWT can call it (documented Wave-1-floor TODO; cannot forge events, only republish signature-valid rows, dedupe makes re-publish a no-op).
- `WebhookIngestBuffer` uses `BoundedChannelFullMode.Wait` (cap 10k) — during a long bus outage (worker drains ~1 row/5s due to connect timeout) the ack path can block, violating the <500ms NFR; runtime-dropped enqueues only recovered at startup scan or manual replay.

## Verified-good (don't re-flag)
- HMAC-SHA256 over raw request bytes, hex decoded then `CryptographicOperations.FixedTimeEquals` — genuinely constant-time, handles hex case, fails closed on missing header/empty secret. `MetaWebhookSignatureVerifier.cs`.
- fb389d5 signature-invalid-replay fix is real at both layers: `ReplayWebhooksHandler` applies `SignatureValid == true` unconditionally (before Id/status branch, bool? semantics exclude null) AND `WebhookProcessor.ProcessAsync` independently refuses `SignatureValid != true` (null-safe). Regression test passes.
- Note: the implementer's "reject-before-deserialize" claim is not literally true — `JsonDocument.Parse` (shape-only, default depth 64) runs BEFORE signature check because invalid-signature rows are persisted to jsonb. Consequence of the forensic-persist design; fixing the Blocking finding fixes this too.
- Dedupe poisoning not possible: only signature-valid rows reach the processor; hash-fallback dedupe keys are SHA-256 of the fragment.
- SQL all parameterized EF LINQ; secrets config-only + fail-closed non-dev; dev/test fixture secrets clearly fake; no wa_id or bodies in logs; RabbitMQ.Client 7.1.2 has no OSV advisories; exchange durable/topic/non-auto-delete.

See [[wavio-security-conventions]] for the platform-wide mechanisms verified during this audit.
