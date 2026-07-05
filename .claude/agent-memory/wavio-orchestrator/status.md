# Orchestrator status — 2026-07-03

## Merge queue (in order; user must merge — agent merges denied by permission classifier)
1. PR #37 — issue #10 (V001–V006 + runner + RLS wiring; core identity boots)
2. PR #38 — issue #11 (CI: build/test, sqlfluff, migration validation, FK-audit)
3. PR #39 — issue #17 (V007–V009 messaging/sessions/templates)
4. PR #40 — issue #12 (VPS baseline: prod compose, Caddy, SOPS/age, backups, envelope cipher)
5. PR #41 — issue #13 (wa-ingest-svc webhook receiver)
6. PR #44 — issue #16 (template lifecycle in wa-admin-svc)
7. PR #43 — issue #15 (session window manager; security + QA passed)
8. PR #45 — issue #14 (outbound gateway; security + QA passed 2026-07-04 — final Wave 1 service)

RUN PAUSED 2026-07-04: all 8 PRs review-complete, awaiting user merges. Next codeable work
AFTER the queue merges: issue #18 offline parts (replay harness — must resolve the parked-event
recovery gap on #18 — and contract tests in CI) together with issue #46 (real-Postgres
integration-test tier, created 2026-07-04). Build #18 off main post-merge, not on the 5-deep stack.

Known dev-DB artifact: QA left an inert test user qa-tenant-user@wavio.local in the shared dev DB
(cleanup was permission-denied; no role/tenant membership, cannot authenticate). Remove
opportunistically when identity seeding is next touched — never bypass permissions to delete it.

#18 planning gap recorded on the issue: parked WaIntel events are NOT recoverable via ingest
replay (no-op for already-published rows) — one side must change in #18's replay-harness work.

Each PR's base is its stack parent; GitHub auto-retargets as parents merge+delete. Merge strictly in order.
All queued PRs passed: my diff review + (where applicable) security audit + QA gate, real-run verification.

## Review gates pattern that worked
implementer (worktree-isolated) → my diff review → security-code-reviewer (adversarial, own repro)
→ fix cycle → security re-verify → qa-test-engineer (independent suite run + one live end-to-end repro)
→ queue. Every round found at least one real defect (fk_audit silent-pass, ingest pre-auth DoS,
Caddy health-check 503, EF insert ordering, transient-DLQ misclassification).

## Deferred/tracked
- Issue #42: pre-first-deploy hardening (audit should-fixes S1/S3/S5/S6 from #40, nits from
  #43/#44/#45, ICurrentTenant X-Tenant-Id gap, masking-regex boundary tests).
- Issue #46: real-Postgres integration-test tier (dispatcher fenced-write path, two-step save).
- Issue #18 comments: replay/sweep race, GET handshake tests, parked-event recovery gap.

## User-side blockers (cannot be done by agents)
- Issue #6: Meta app setup (Embedded Signup, test WABA + INR numbers) — blocks live acceptance
  runs of #13/#14/#16 and all of #18's live scenarios.
- Issue #7: open decisions OD-1 (AI provider policy, blocks Wave 4), OD-2 (number strategy,
  blocks Wave 3 G2). OD-3 effectively decided by merged scaffold (custom CQRS).
- Merging the PR queue (permission classifier requires user intent).
- Issue #12's VPS-dependent acceptance: real domain/LE cert, Meta webhook verify, restore drill on VPS.

## Wave 1 remaining after #15
- #14 gateway (needs #15's window checks — briefing after #15 passes gates)
- #18 offline parts (replay harness, contract tests in CI) after #14
