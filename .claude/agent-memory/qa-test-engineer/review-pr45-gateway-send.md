---
name: review-pr45-gateway-send
description: QA review record for PR #45 (issue #14, wa-gateway-svc outbound send API, final Wave 1 gate) — verdict APPROVE; live-proved both idempotency paths (incl. the untested concurrent race) and the crash-reclaim scenario against real Postgres; flagged OutboxDispatcherService's total lack of unit coverage as a structural (not negligence) gap
metadata:
  type: project
---

# PR #45 review (issue #14 — wa-gateway-svc outbound send API), reviewed 2026-07-04

Base `feature/15-session-windows`. Final Wave 1 QA gate. Security approved,
four should-fixes landed (commit `8aae0b1`). Scoped to test quality and
acceptance-criteria coverage only.

## Verdict: APPROVE (no blocking defects; one structurally-significant
test-coverage finding, correctly explainable and not a sign of negligence;
otherwise excellent, precise test quality)

## What I independently verified
- **139/139 across all three services** (68 WaGateway + 35 WaIntel + 36
  WaIngest), matches claim exactly. `dotnet build wavio.slnx` — 0 errors.
- **`WindowPolicyEvaluatorTests`**: exhaustive ADR-005 branch coverage — all
  9 non-template types confirmed free-form, both CS/CTWA-open combinations
  for free-form allow, marketing (always billable, both window states),
  authentication (always billable even inside an open window — the one
  template category the window doesn't cover, per spec §2.2), utility
  (free inside, billable outside). Pure static evaluator, real assertions.
- **`DependencyInjectionTests`** (the S1 Graph-timeout-vs-stale-lock
  invariant): precisely tested at the boundary — equal values reject,
  timeout > stale-lock rejects, explicit headroom passes, computed default
  passes, full defaults pass. This is exactly the kind of boundary-precise
  testing I look for and it's here.
- **`SendMessageHandlerTests`**: rejected-duplicate-returns-same-rejection,
  per-tenant key scoping, cross-tenant/unmatched phone-number-id guard (S3)
  — all real state assertions against the in-memory EF context.
- **`BootGuardsTests`**, **`GraphErrorClassifierTests`** (all three permanent
  codes 131026/131047/131049 + 429/5xx transient), rate limiter tests — all
  solid, real-behavior assertions.

## The headline finding: `OutboxDispatcherService` — the class implementing
BOTH acceptance criteria — has ZERO automated test coverage, and it's a
structural EF-provider limitation, not an oversight

Confirmed by reading the class: every single state-transition write in
`OutboxDispatcherService` (lease claim, attempts checkpoint, completion,
backoff, dead-letter) is a fenced `ExecuteUpdateAsync(...WHERE locked_by =
_instanceId AND status = 'dispatching')` call — this IS the fenced-write
mechanism the security review added (S1). **Empirically confirmed** (via a
temporary probe test, removed after) that `ExecuteUpdateAsync` throws
`InvalidOperationException: ... not supported by the current database
provider` against the EF Core **InMemory** provider used by
`InMemoryWaGatewayDbContext` — the same pattern used by every other test
fixture in this codebase. This means: with the current testing conventions
(InMemory provider for all Application-layer unit tests), **this entire
class cannot be unit-tested at all** — not the lease-claim race, not the
"0 rows affected → discard" loser path, not the backoff/dead-letter
transitions. It's proven exclusively by the PR's live-verification
transcript (staged crash-state reclaim) and by my own live reproduction
below.

This is NOT a sign of the implementer skipping tests — it's a genuine
architectural constraint of the testing pattern used everywhere in this
codebase (same EF-InMemory-can't-do-X shape as PR #44's two-step-save
finding, but here the blast radius is an entire safety-critical background
service class, not one method). Flagging prominently because: (a) it's the
class both acceptance criteria hinge on, and (b) a future refactor of this
class has no regression net at all beyond re-running live verification by
hand.

**Suggestion** (not blocking, for whoever owns test infra next): a
lightweight real-Postgres integration test tier (even the same bare
`postgres:16` container pattern the CI migration-validation job already
uses) would let `OutboxDispatcherService`-shaped classes get real regression
coverage. Not something to demand of this PR specifically.

## Live reproductions (both against real Postgres, no mocks)

Hit the same permission boundary as [[review-pr43-session-windows]]
attempting the literal HTTP+JWT approach (`POST /v1/messages` requires
`permission:messages.send` AND a resolved tenant JWT — same missing-piece as
before). Pivoted the same way: exercised the real production classes
directly against real Postgres, bypassing HTTP/JWT/RBAC entirely (see
[[environment]] for the general pattern).

1. **Staged crash-state reclaim** (the issue's headline criterion): built
   and ran the real `WaGateway.WebApi` (hosting the real
   `OutboxDispatcherService` as a background service, `StaleLockSeconds=10`)
   plus the real `tools/MetaGraphSendApiStub`. Inserted an
   `outbound_messages` + `outbound_outbox` row pair directly via SQL, staged
   exactly as a crash would leave it: `status='dispatching'`,
   `locked_by='crashed-instance-xyz'`, `locked_at = now() - 1 minute`
   (stale), `attempts=1`. Within 5s: the running instance's *own*
   `locked_by` (`Goutam's-MacBook-Pro:<pid>:<guid>`) claimed it, `attempts`
   went 1→2 (confirms the pre-dispatch checkpoint fired), `status` went
   `dispatching`→`dispatched`, and `outbound_messages` got a real `wamid`
   from the stub with `status='dispatched'`. Zero message loss, reclaim
   proven end-to-end with the real dispatcher, not a simulation. Cleaned up
   the test rows after.
2. **Duplicate Idempotency-Key with a different body → original result,
   including the never-before-tested concurrent race**: instantiated the
   real `SendMessageHandler` directly against a real Postgres-backed
   `WaGatewayDbContext` (bypassing RLS via the superuser role — safe here
   because the handler's own query already filters by `TenantId`
   explicitly, RLS is defense-in-depth on top of that, not the only guard).
   - Sequential: sent once, resent the identical key with a completely
     different body — `Id`/`Status`/`Wamid`/`ErrorCode` all identical to the
     first call.
   - **Concurrent** (the specific gap `InMemoryWaGatewayDbContext`'s own doc
     comment admits it can't cover): fired two `HandleAsync` calls truly
     concurrently, same idempotency key, two independent `DbContext`
     instances (simulating two racing HTTP requests). Result: no unhandled
     exception escaped either call, both returned the **same** message Id,
     and exactly **one** row exists in `outbound_messages` for that key —
     the real unique-index violation on the loser was caught and correctly
     resolved to the winner's row. This is the strongest single result of
     this review: it directly proves the exact mechanism the checklist
     worried was unverified, against the real constraint, not a mock.

Both probes were temporary files, removed after (`git status`/`git diff`
clean each time); all test DB rows cleaned up afterward.

## Input for #18 (not blockers for #45)
- Real Graph sends (this PR's Graph client talks to a local stub server, not
  real Meta) and real 429/tier behavior — the retry/backoff and
  token-bucket/tier-gate logic is unit-tested and live-proved against the
  stub, but real Meta throughput/tier numbers and real rate-limit responses
  are unverified until #18.
- Template sends with real approved templates (this PR's template category
  is caller-declared, not looked up from wa-admin-svc's catalog — a
  deliberate Wave 1 scope cut, not a defect).
