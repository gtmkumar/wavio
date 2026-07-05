---
name: review-pr41-ingest-webhooks
description: QA review record for PR #41 (issue #13, wa-ingest-svc webhook receiver) — verdict APPROVE, live acceptance-criteria reproduction, test-coverage gaps noted as nits/input for #18
metadata:
  type: project
---

# PR #41 review (issue #13 — wa-ingest-svc webhook receiver), reviewed 2026-07-03

Base `feature/17-wave1-migrations`. Security already approved (two rounds, six
findings fixed with regression tests) — this review scoped to test quality and
acceptance-criteria coverage only, per orchestrator instruction not to
re-litigate security/architecture.

## Verdict: APPROVE (no blocking findings; nits only, one item flagged as
input for #18 rather than a fix-now)

## Method note: reviewed via a scratchpad worktree, never touched the shared
repo dir

Standing policy at this point in the project: other agents run concurrently
in `/Users/gtmkumar/Documents/source/wavio` (issue #12/#15 work, uncommitted
changes present throughout this review). Used `git worktree add --detach
<scratchpad>/pr41-worktree origin/feature/13-ingest-webhooks`, built/tested/ran
the service entirely there, removed the worktree after. See [[environment]]
for the reusable recipe. Confirmed via `git status --porcelain` before/after
that the shared dir's modified-file set was untouched by me (it kept changing
— from other agents' concurrent work — throughout, which is expected and not
mine).

## What I independently verified
- **36/36 tests pass** (`dotnet test tests/WaIngest.Tests`), matching the
  implementer's claim exactly.
- **Test quality is real, not mocks-returning-mocks**: `WebhookProcessorTests`
  uses an actual in-memory `IWaIngestDbContext` and asserts real state
  (`ProcessingStatus`, `db.WebhookDedupes.Count()`), mocking only
  `IEventBusPublisher` — the one legitimate external boundary. Same pattern in
  `ReceiveWebhookTests` (fabricated `DefaultHttpContext`, asserts the actual
  persisted command payload never contains the attacker's marker string).
  `RabbitMqConnectionManagerTests` has three clean cases for the S2 fail-closed
  regression (Production-missing→throws, Development-missing→falls back,
  explicit-config→never throws regardless of environment).
- **Live acceptance-criterion reproduction — duplicate webhook produces one
  bus event**: built+ran `WaIngest.WebApi` from the worktree against the
  shared dev Postgres/RabbitMQ, POSTed one signed payload twice (identical
  bytes, real HMAC-SHA256 signature). Result: 2 rows in `ingest.raw_webhooks`
  (both `processed` — correct, every delivery is persisted), but exactly
  **1** row in `ingest.webhook_dedupe`, and the RabbitMQ management API's
  `wavio.events` exchange `publish_in` counter moved by exactly **+1**
  (9→10) — two independent lines of evidence, not just the DB dedupe row.
  Cleaned up the test rows from the shared DB afterward (`DELETE ... WHERE
  wamid = '<test-wamid>'`).
- **GET subscription handshake** (`VerifySubscription`): manually curl'd all
  three branches (correct mode+token→200+raw challenge echoed; wrong
  token→403; wrong mode→403) against the live running instance — all correct.
  **Not covered by any automated test** (see gaps below).
- **Malformed JSON "async path"**: traced this rather than just testing it —
  the HTTP endpoint (`Webhooks.ReceiveWebhook`) already runs
  `JsonDocument.Parse` on the signed body and returns `400` *before*
  persisting anything, so `ProcessCoreAsync`'s own `JsonException` catch
  (the actual "async path" code) is unreachable via any real HTTP delivery —
  it's defensive-only (e.g. if a row's payload were ever corrupted after
  persist, or a future direct-DB-insert tool bypassed the endpoint).
  Confirmed empirically: a signed-but-malformed-JSON POST gets `401`... no,
  gets `400` at the HTTP layer, never reaches `ingest.raw_webhooks`. **Not a
  real gap** — noted as a clarification, not a defect.

## Gaps found in test coverage (nits, not blocking)
1. **GET handshake has zero automated test coverage** despite being a pure,
   trivially-testable static method (same shape as `ReceiveWebhook`, already
   tested that way). Manually verified correct (see above) but should get a
   `VerifySubscriptionTests.cs` at some point — small, cheap, not worth
   blocking merge over given it manually checks out and isn't part of the
   issue's 3 stated acceptance criteria.
2. **Dedupe race — a real (if narrow) one exists between the replay endpoint
   and the live background worker, not tested anywhere**: `WebhookProcessor`'s
   class doc correctly argues no race exists in the *normal* pipeline (single
   background worker, sequential). But `ReplayWebhooksHandler`'s default
   scope selects rows with `ProcessingStatus == "received"` with **no
   staleness filter** (unlike the periodic sweep's 5-second `StaleWindow`) —
   so a broad/default `POST .../replay` call issued while webhooks are
   actively arriving could pick up a row still mid-flight in the worker's
   buffer, and both would run their own SELECT-then-publish-then-INSERT
   sequence in independent `DbContext` scopes, risking a genuine duplicate
   bus publish. Severity is low: replay is platform_admin-only (not
   attacker-reachable), the window is narrow, and the platform-wide contract
   already requires consumers to be idempotent on `EventId` (documented in
   `WebhookProcessor`'s own remarks) specifically to tolerate at-least-once
   delivery — so even if this fires, it's within the system's designed
   tolerance, just not the exact "duplicate webhook" scenario the AC is
   about (that one — Meta redelivery — is solidly proven, see above).
   **Recommend as input for #18's live smoke suite** (concurrent replay
   during live traffic), not a blocker for #41 — matches the orchestrator's
   framing exactly.

## Not independently re-verified (time/priority tradeoff, noted as
assumption)
- p99 ack latency load test — trusted the implementer's `ab` numbers (p99
  55ms via 2000 req/c50) rather than re-running; the orchestrator's message
  explicitly prioritized the duplicate-wamid scenario as "most prone to
  false confidence," which I did reproduce live. The load-test claim is
  also less prone to fabrication (specific ab output shape, consistent with
  the async-ack design) and re-running it doesn't change acceptance-criteria
  risk the way an unverified dedupe claim would.
- Replay-from-raw end-to-end against real RabbitMQ — covered meaningfully by
  `ProcessAsync_ReplayAfterBusRecovers_PublishesAndMarksProcessed` (real
  state assertions, not a mock check) and the implementer's own live
  transcript; didn't re-run live myself given time spent on the duplicate
  scenario + GET handshake + gap analysis.

## Input for #18 (Wave 1 live smoke suite) — not a blocker for #41
- The replay-vs-live-worker race above is exactly the kind of scenario real
  Meta traffic timing can't be faked for in a unit test — a good candidate
  live scenario: fire a burst of real/simulated webhooks, then call
  `POST /replay` with a broad time window *during* the burst, and assert the
  bus/dedupe-row count stays 1:1 with unique wamids despite the overlap.
  Whether the low-severity risk noted above should actually be design-fixed
  (e.g. give the replay handler the same `StaleWindow` exclusion the sweep
  uses) is an implementer/architecture call, not something I'm asserting
  here — flagging it as something #18 should be able to observe either way.
- `billing.message_costs` (deferred to #19) and tenant resolution (documented
  out of scope) are orchestrator rulings, not gaps — no action needed from
  QA on either.
