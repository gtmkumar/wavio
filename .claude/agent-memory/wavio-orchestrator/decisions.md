# Orchestrator decisions

## 2026-07-03 — Merge permission denial (process constraint)

`gh pr merge 37 --squash` was denied by the permission classifier as self-approval:
"teammate-message merge authority does not establish user intent" — merging PRs authored by
agents I control requires the user (or a settings permission rule). PR #36 had merged without
denial earlier the same day, so treat merge permission as per-action, not durable. Standing
response: report the denial to team-lead, never route around it, keep developing subsequent
issues as stacked branches so work doesn't idle behind an unmerged PR.

## 2026-07-03 — Stacked-branch regression hazard (PR #43 S1)

WaIntel's RabbitMqConnectionManager regressed the fail-closed pattern already fixed in WaIngest
(PR #41 S2) because feature/15-session-windows was cut from feature/13-ingest-webhooks BEFORE
that fix landed. LESSON, now part of every implementer brief: when a branch is cut from a
pre-fix parent, diff any copied infra boilerplate (connection managers, auth wiring, consumers)
against the LATEST reviewed pattern on sibling branches before reusing it. Reviewers: check
new copies of previously-audited boilerplate for regressions first.

## 2026-07-03 — Parallel agents MUST use isolated git worktrees (incident)

While #12 (teammate) and #13 (subagent) ran concurrently, both operated in the shared repo
checkout; the #12 agent's branch checkouts/resets silently undid the #13 agent's first
unpushed commit. #13 recovered via `git worktree` with no loss. STANDING RULE: any time two
implementers run concurrently, every brief must mandate an isolated `git worktree` (or the
Agent tool's worktree isolation) and forbid branch checkouts in the shared working dir.
Reviewers running concurrently with implementers must review via `gh pr diff`/`git show`
or their own worktree — never checkout in the shared dir.

## 2026-07-03 — Issue #13 pricing→billing.message_costs deferred to Wave 2

Issue #13 (ingest) lists "pricing object from status webhooks written to billing.message_costs",
but the billing schema is Wave 2 (#19/#23) and does not exist in V001–V009. Ruling: that sub-task
moves to Wave 2's cost-ledger work (#19). Wave 1 requirement instead: the normalized
wa.message.status.v1 event AND ingest.raw_webhooks must carry/retain the complete raw pricing
object so the Wave 2 ledger can be backfilled from raw if needed. No data loss, no premature
schema.

## 2026-07-03 — Wave 1 service sequencing

#13 (ingest) first — it defines most WaPlatform.Contracts events, which #14/#15/#16 consume.
Then #15 (window manager, consumes wa.message.received), then #14 (gateway, consults #15 per
ADR-005), then #16 (templates). Avoids add/add conflicts in the shared Contracts project.

## 2026-07-03 — Schema ownership: template schemas vs spec §6 (issue #10)

Conflict found before briefing #10: core identity (template) maps `identity_access` (13 tables),
`tenancy_org` (tenants), `kernel` (outbox, feature_flags, system_settings, file_attachments) in
`wavio.SharedDataModel/Persistence/`, while spec §6 defines `tenancy`, `waba`, `ingest`, `system`.
The template's Tenant entity doc-comment explicitly says it's an example to rename/extend.

**Ruling:**
1. ONE canonical tenants table: `tenancy.tenants` (spec §6 wins). Core identity's Tenant entity is
   remapped from `tenancy_org.tenants` → `tenancy.tenants`; `tenancy_org` schema is dropped from
   the codebase. Rationale: RLS scoping unit must be a single source of truth; Wave 1+ wa-* tables
   FK to tenants — two tenant tables would drift.
2. `identity_access` stays core-identity-owned as-is (users/RBAC/refresh tokens/its own
   identity-scoped audit_logs). DDL delivered in #10 as V005 so core boots.
3. `kernel` stays core-identity-owned infrastructure (outbox, system_settings, file_attachments).
   DDL as V006. Platform feature flags are canonical in `system.feature_flags` (spec); prefer
   remapping core's FeatureFlag entity to `system.feature_flags` and NOT creating
   `kernel.feature_flags` — fallback (if wiring is disproportionate): keep kernel.feature_flags as
   identity-internal and document the split.
4. Migration numbering: V001 tenancy, V002 waba, V003 ingest, V004 system (per issue text),
   V005 identity_access, V006 kernel. Issue #17's "V005–V008" labels shift to next-available
   numbers — issue titles are planning labels, actual file numbers are assigned at implementation.

## 2026-07-03 — PR #36 (issue #9) merged

Reviewed against issue #9 + spec, squash-merged (main @1910efd). Soft leftover from #9's task
list (migration-runner convention + RLS pattern documentation) deliberately folded into #10's
brief rather than blocking #9 — the mechanism is built in #10, docs belong with it.

## 2026-07-03 — Migration runner mechanism

Issue #10 defines the mechanism; tool choice delegated to dotnet-backend-dev with constraints:
versioned V00N__*.sql applied in order, tracked in a migrations table, forward-only, runnable
locally (compose) + in CI (#11) + on VPS deploy (#12). DbUp-style .NET console runner suggested.
