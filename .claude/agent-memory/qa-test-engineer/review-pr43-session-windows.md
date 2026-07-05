---
name: review-pr43-session-windows
description: QA review record for PR #43 (issue #15, wa-intel-svc Session Window Manager) — verdict APPROVE; live-proved cross-instance cache invalidation via the real listener class (not full HTTP/JWT); found the ingest-replay recovery path doesn't actually redrive a parked WaIntel consumer
metadata:
  type: project
---

# PR #43 review (issue #15 — wa-intel-svc Session Window Manager), reviewed 2026-07-03

Base `feature/13-ingest-webhooks`. Security approved (two should-fixes landed:
fail-closed RabbitMQ config + wa_id path masking). Scoped to test quality and
acceptance-criteria coverage only.

## Verdict: APPROVE (no blocking defects; one real cross-service design gap
worth surfacing, correctly scoped as #18 input per the task; small test nits)

## Method: scratchpad worktree (see [[environment]]); hit a permission
boundary partway through live verification — see below, handled by pivoting
method, not by working around the denial.

## What I independently verified
- **35/35 WaIntel.Tests pass** (`dotnet test`), matches claim exactly.
  36/36 WaIngest.Tests also pass (regression safety, this branch's base).
  `dotnet build wavio.slnx` — 0 errors (this PR made no specific warning-count
  claim, unlike #44, so nothing to cross-check there).
- **`WindowRulesTests` precision**: expiry boundary tested at exactly -1/0/+1
  seconds from the instant (0 = not open — strictly-future required, matches
  the doc comment); double-notify guard tested both ways (already-notified →
  false, reset-to-null-after-extension → true again). One small gap: the
  `IsApproachingClose` horizon itself isn't tested at the exact boundary
  (`expiresAt == now + horizon`, which the implementation's `<=` treats as
  inclusive/true) — only "well within" (1h of 2h) and "well beyond" (3h of
  2h) are covered. Nit, not blocking — logic is trivial and I confirmed by
  reading `WindowRules.IsApproachingClose`'s `<=` operator directly.
- **`WaPiiMaskTests` digit-boundary gap — proven empirically, not just
  read**: the shipped tests never hit the exact E.164 boundary lengths (9,
  10, 15, 16 digits) the regex (`\d{10,15}` with lookaround) is built around
  — existing tests use "42" (2 digits) and always-12-digit examples. I
  temporarily added a probe test (never committed) constructing exactly
  9/10/15/16-digit runs and confirmed empirically: 9 → untouched, 10 →
  masked, 15 → masked, **16 → untouched entirely** (not partially masked —
  worth knowing, since it's non-obvious regex backtracking behavior: the
  `(?<!\d)` lookbehind forces the match to start at the beginning of a
  digit run, and a contiguous 16-digit run can never satisfy both the
  lookbehind at its start and the `{10,15}` cap without a trailing digit
  breaking the `(?!\d)` lookahead, so the whole run is skipped, not
  truncated-and-masked). This is CORRECT behavior (16+ digit runs are
  definitionally not E.164 wa_ids) but currently unverified by any test, and
  the correctness depends on non-obvious backtracking that a future "simplify
  the regex" edit (e.g. switching to a lazy quantifier) could silently break
  with nothing catching it. Removed the probe file after (`git status`/
  `git diff` clean).
- **Cache tests are real, not mock-echo**: `GetWindowStateHandlerTests` uses
  a genuine `Microsoft.Extensions.Caching.Memory.MemoryCache` (not a mock)
  plus the real in-memory EF DB — hit/miss/populate/invalidate-then-refetch
  all assert actual cache and DB state. The LISTEN/NOTIFY wiring itself
  (`WindowCacheInvalidationListener`) is deliberately not unit-tested (needs
  real Postgres) — that's exactly the live scenario I reproduced below.
- **`UpsertWindowOnMessageReceivedHandlerTests`**: CS reset per message,
  CTWA entry (72h from referral, independent of CS), a non-referral
  follow-up correctly leaving the CTWA expiry untouched while still
  refreshing CS, closing-notify guard reset on extension, and the exact
  NOTIFY channel/payload format (`"conversation_window_changed"`,
  `"{tenantId}:{phoneNumberId}:{waId}"`) asserted via a fake notification
  sink. All real state assertions.

## Live reproduction — cache invalidation across two instances (the
orchestrator-suggested, highest-priority scenario)

**What happened**: attempting the suggested approach literally (two full
`WaIntel.WebApi` HTTP instances + a tenant-scoped JWT to call
`GET /v1/windows/{waId}`) required minting a real tenant-scoped user, since
the only seeded account is `platform_admin` (whose token carries no
`tenant_id` claim — confirms the exact gap PR #44 already flagged:
`HttpContextCurrentTenant` doesn't get a usable tenant context for
platform_admin). Creating a user via the admin API succeeded; **granting it
a tenant role via `POST .../change-role`, and later even deactivating that
test user for cleanup, were both denied by the environment's permission
classifier** as out-of-scope identity/RBAC mutations on shared state — a
correct call, and I did not attempt to route around it (no SQL-level
workaround). One inert leftover: a `qa-tenant-user@wavio.local` account
exists in the shared dev DB with no role/tenant membership (so it cannot
meaningfully authenticate as anyone) — flagged here for the user/orchestrator
to remove if desired; I could not clean it up myself.

**Pivoted method**: rather than fight the auth layer, I verified the actual
mechanism under test — cross-instance cache convergence via Postgres
LISTEN/NOTIFY — directly, using the *real* production
`WindowCacheInvalidationListener` class (no HTTP, no JWT, no RBAC touched):
instantiated it twice against two independent `IMemoryCache`s (simulating
two live instances), pre-seeded both with a stale cache entry for the same
key, issued one real `NOTIFY conversation_window_changed, '...'` against the
shared Postgres (read-only pub/sub, no table writes), and measured both
caches evicting the stale entry. **Result: both converged in 29ms** —
comfortably inside the claimed ~1s bound. This is a faithful test of the
exact mechanism (the real listener class, real Postgres NOTIFY, two
independent cache instances) without the unrelated HTTP/JWT/RBAC plumbing
getting in the way. Removed the throwaway test file afterward (verified
clean via `git status`/`git diff`).

## Checklist item 4 — is the parked-event recovery path actually
exercisable today? **No, and it's a real cross-service gap — flagging as
#18 input, not a blocker for #43** (per the task's own instruction)

Traced this across both PR #41 (wa-ingest-svc, already reviewed) and this
PR's `WabaPhoneNumberTenantResolver`/`MessageReceivedConsumerService`:
- WaIntel's design assumption: "wa-ingest-svc's `raw_webhooks` is the
  durable source of truth this could be replayed from once onboarding
  ships."
- But wa-ingest-svc's `WebhookProcessor` (PR #41) records a
  `ingest.webhook_dedupe` row **after a successful bus publish**, and its
  replay path (`ReplayWebhooksHandler` → `WebhookProcessor.ProcessAsync`)
  skips re-publishing entirely once that dedupe row exists — that's
  correct and intentional from wa-ingest-svc's own point of view (it
  already delivered the event to the bus once; that's its whole job).
  The dedupe key is `(wamid, event_type)`, not per-consumer.
- Today, WaIntel's `MessageReceivedConsumerService` DID receive
  `wa.message.received.v1` successfully (the bus delivery worked) — it's
  WaIntel's own tenant-resolution step that parks it (acks, drops). From
  wa-ingest-svc's perspective this event was successfully published and
  will never be replayed again by the standard replay tool, because
  "already published" is exactly the condition replay is designed to
  short-circuit on.
- **Consequence**: once #6 (WABA onboarding) ships and `waba.phone_numbers`
  starts getting rows, a message that arrived and got parked *before* the
  matching phone number was onboarded will NOT be automatically recovered
  by calling wa-ingest-svc's replay endpoint — replay is a no-op for it.
  There is currently no mechanism (on either service) to say "redrive this
  specific event again for this one downstream consumer that parked it,
  even though the bus delivery itself already succeeded."
- Not a defect in either PR individually — each service's design is locally
  correct and well-reasoned for its own stated purpose. It's a gap that only
  shows up when tracing the two together, and only matters once #6 lands.
  **Recommend as a concrete #18 (or #6 handoff) scenario**: verify that a
  parked window-consumer event for a since-onboarded phone number actually
  gets recovered, and if it doesn't (my analysis says it won't), decide who
  owns fixing it — e.g. a WaIntel-side periodic re-scan of parked/dropped
  events independent of wa-ingest's dedupe, or a "force" replay mode.

## Input for #18 planning (not blockers for #43)
- Real CTWA referral traffic: wa-ingest-svc's normalizer doesn't populate
  `MessageReceivedV1.Referral` yet (already an acknowledged, flagged gap in
  this PR's own description) — CTWA window opening can only be proven with
  real Meta traffic once that's wired up; the synthetic-referral test here
  proves the window-logic mechanism only.
- The parked-event replay gap above.
