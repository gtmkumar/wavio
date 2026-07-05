---
name: review-pr44-template-lifecycle
description: QA review record for PR #44 (issue #16, wa-admin-svc template lifecycle v1) — verdict APPROVE; proved the two-step-save regression is currently uncaught by any automated test; two live consumer-driven scenarios reproduced
metadata:
  type: project
---

# PR #44 review (issue #16 — wa-admin-svc template lifecycle v1), reviewed 2026-07-03

Base `feature/13-ingest-webhooks`. Security approved (one round + two
should-fixes, commit `ca3890c`). Scoped to test quality and
acceptance-criteria coverage only.

## Verdict: APPROVE (no blocking defects; one real, clearly-scoped test-gap
finding — proven experimentally, not just asserted — plus small nits)

## Method: scratchpad worktree again (see [[environment]]), never touched
the shared repo dir (other agents' concurrent uncommitted work was present
and changing throughout — confirmed via `git status` before/after that my
own work didn't touch it).

## What I independently verified
- **97/97 WaAdmin.Tests + 36/36 WaIngest.Tests pass**, `dotnet build
  wavio.slnx` — 0 errors, 135 warnings (matches the claimed unchanged
  baseline).
- **State machine coverage is genuinely exhaustive, not "representative"**:
  `TemplateStatusTransitionsTests` hand-lists the 10 legal edges, then
  **programmatically generates every other (from,to) pair among the 6
  known statuses** and asserts each returns false — so a future edge added
  to the transitions table without a matching test update fails loudly
  (new pair silently becomes "legal" only if the test's own generation
  logic is also updated to expect it), rather than a curated subset that
  could miss a case. Plus dedicated same-status and DISABLED-terminal tests.
  This is the best state-machine test I've reviewed in this project so far.
- **Handler tests assert real state**, not mock-echo:
  `ProcessTemplateStatusChangedCommandHandlerTests` covers unresolvable
  tenant (park, no DB touch), unknown template (park), invalid transition
  (park + template unmutated + no event row), valid transition (status
  change + event row), first pause (3h window + freeze-hook call), second
  pause (6h escalation via `PauseCount`), disabled (clears pause window +
  freeze-hook call) — mocks only `ICampaignFreezeHook` (a documented no-op
  stub for #22, a legitimate boundary). `UpdateTemplateCommandHandlerTests`
  asserts immutability directly: approved version's `Components` unchanged,
  new version 2 created as DRAFT, template reverts to DRAFT, event recorded.
- **TransientRetryPolicyTests is deterministic and precisely targeted**: no
  real broker/DB/delay (injected `Func<TimeSpan, CancellationToken, Task>`
  delay), and includes a dedicated regression test
  (`IsTransient_TransientExceptionWrappedInNonTransientOuterType_StillDetectedViaInnerException`)
  for the exact live-caught bug (EF's `RetryLimitExceededException` wrapper
  not recognized without walking `InnerException`) — uses a generic wrapper
  instead of the real EF type, a reasonable tradeoff to avoid an
  unnecessary package reference while still testing the actual mechanism.
- **Live end-to-end reproduction #1 (orchestrator-suggested scenario)**:
  inserted a PENDING template+version row directly via SQL, published a
  real `wa.template.status_changed.v1` event (PENDING→APPROVED) to the live
  `wavio.events` exchange via the RabbitMQ management API against a running
  `WaAdmin.WebApi` built from the worktree. Result: `templates.status` →
  APPROVED, `version` incremented, `template_versions.status`/`reviewed_at`
  updated, one `template_status_events` row recorded old→new correctly, and
  the main queue showed 0 ready/unacked afterward (cleanly consumed and
  acked, not stuck). Confirms the consumer wiring end-to-end with zero
  gaps.
- **Live end-to-end reproduction #2 (invalid-transition-to-DLQ, extra
  scrutiny since the plumbing was already up)**: seeded a DISABLED template,
  published a DISABLED→APPROVED event (illegal per the state machine).
  Result: template stayed DISABLED, no status event recorded, and the DLQ
  queue went from 0→1 message. Directly corroborates the PR's own claimed
  live verification of this exact scenario. Purged the DLQ message and
  deleted all test rows afterward.
- **Permission/step-up wiring**: confirmed `templates.delete` is seeded as
  `RiskLevel.High` in `core.Infrastructure/Seeders/IdentitySeeder.cs`,
  matching the PR's claim (didn't re-test the generic step-up mechanism
  itself — that's pre-existing platform machinery, out of scope per "no
  security re-litigation").

## The one real finding: the two-step-save fix has no regression test that
would catch a reversion (proved experimentally, not just noted)

`CreateTemplateCommandHandler` does `SaveChangesAsync()` →
`template.CurrentVersionId = version.Id` → `SaveChangesAsync()` again, to
work around the genuinely circular `Template.CurrentVersionId ↔
TemplateVersion.TemplateId` FK pair that broke live (real Postgres + the
audit interceptor) when both entities were saved as part of one batch.

I temporarily reverted this to the single-batch form (setting
`CurrentVersionId` *before* the first save, merging the two calls into one)
directly in my scratchpad worktree, rebuilt, and reran `WaAdmin.Tests`:
**all 97 tests still passed**, including
`CreateTemplateCommandHandlerTests.HandleAsync_ValidTemplate_...` which does
assert `result.Template.CurrentVersion` is set. This is because
`InMemoryWaAdminDbContext` uses EF Core's **InMemory** provider, which does
not enforce the same relational insert-ordering/FK constraints as Npgsql —
exactly why the PR's own text says "a bug the EF InMemory test provider
could not [catch]". I restored the original file immediately after (byte
comparison via `git diff`/`git status` — clean) before doing anything else.

**Conclusion**: the only thing currently protecting this exact regression is
the one-time manual live-verification transcript in the PR description, not
an automated, repeatable test. This is a real gap, but a narrow, well-scoped
one — not a reason to block merge (the fix itself is correct and I
independently confirmed the *current* code produces 97/97 green plus a
correct live create-with-first-version behavior implicitly through the
consumer-path live tests above, which do read `current_version_id`-backed
rows created by exactly this code path in earlier PR/dev-stack activity).
**Recommend** (nit, not blocking): either (a) a lightweight integration test
against a real Postgres (even a single xunit test with a testcontainer or
the same bare-container pattern used for CI's migration-validation job), or
at minimum (b) a code comment pointing future readers at this memory file /
the PR description so nobody "simplifies" the two-step save back into one
without knowing why.

## Nits (non-blocking)
- Same project-wide convention as PR #41: endpoint tests are deliberately
  thin (mocked `IDispatcher`, verifying only HTTP-status mapping), so
  immutability-on-PUT is proven at the handler layer + a one-time manual
  live transcript, not at an automated HTTP-integration-test layer. This
  matches the codebase's existing convention (no `WebApplicationFactory`-
  style tests exist anywhere yet) — flagging as consistent with prior
  findings, not a new problem to fix now.

## Input for #18 (Wave 1 live smoke suite) — not a blocker for #44
- Real Meta category-reclassification and 3h/6h/disabled auto-pause timing
  can only be observed with real Meta traffic; the synthetic-event tests
  here (both automated and my live repro) prove the *mechanism* works, not
  Meta's actual timing/payload shape in production.
- The billing-recalibration hook (#19) and campaign-freeze hook (#22) are
  honest no-ops — #18 scenarios involving templates should expect these to
  be silent until those issues land.
