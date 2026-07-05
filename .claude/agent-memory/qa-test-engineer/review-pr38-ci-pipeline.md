---
name: review-pr38-ci-pipeline
description: Full QA review record for PR #38 (issue #11 CI pipeline) — verdict, what was verified, the one defect found, and its fix re-verified
metadata:
  type: project
---

# PR #38 review (issue #11 — CI pipeline), reviewed 2026-07-03

## Update: fix re-verified, final verdict APPROVE (2026-07-03, same day)

Implementer pushed `07e0d01` (fix) + `fe93f55` (memory note) on
`feature/11-ci-pipeline` in response to the blocking finding below. Fix:
`db/tests/fk_audit.sh` replaced both `mapfile -t arr < <(psql_q ...)` reads
with a `psql_lines()` helper — `raw="$(psql_q ...)" || { echo ...; exit 1; }`
then `mapfile -t arr <<< "$raw"` guarded by `[[ -n "$raw" ]]` for the
empty-result edge (avoids the classic "one phantom empty element from
`mapfile <<< \"\"`" bug).

Re-verified independently (fetched `origin/feature/11-ci-pipeline`, copied
the script + allowlist to a scratch dir, fresh bare postgres:16 on port
55433, never touched repo-tracked files):
- **Bad-password case (the exact repro that broke before)**: now exits 1
  with `[fk_audit] psql query failed — aborting (see error above).` — matches
  the implementer's claim exactly.
- **Good path**: `Checked 53 uuid *_id column(s); 15 allowlisted` → exit 0,
  byte-identical result to pre-fix behavior — no regression.
- **Allowlist-removal negative case**: re-ran for completeness (cheap,
  same script paths) — still fails naming
  `kernel.outbox_events.aggregate_id`, exit 1.
- New Actions run `28661908138` — all 4 jobs green.
- Repo tree left clean throughout (`git status --porcelain` empty except
  pre-existing untracked dirs unrelated to this review); all scratch
  containers/dirs removed after.

No new issues found in the fix itself — the `psql_lines` helper is correctly
scoped (local nameref, resets the array before repopulating) and the
empty-result guard is exactly right for bash's `mapfile <<<` quirk. **Final
verdict: APPROVE, merge-ready.**

## Original review verdict (superseded above): REQUEST CHANGES (one concrete, small fix; everything else is solid)

The workflow design is sound and every claim in the PR description was
independently reproduced. One real defect: `db/tests/fk_audit.sh` can
silently report success ("Checked 0 columns") if its `psql` query fails,
because `mapfile < <(...)` process substitution isn't covered by
`set -euo pipefail`. Full technical detail in [[fk-audit-gate-notes]].

## What I independently verified (not just re-reading the PR's claims)
- `dotnet build` on `wavio.slnx` — 0 errors (135 CA warnings, pre-existing,
  not introduced by this PR — this PR touches no C# source).
- `dotnet test` on the same solution (no test projects yet) — exits 0,
  confirms the "green hook for later waves" claim.
- `uvx sqlfluff==4.2.2 lint db/migrations/` — clean.
- Migrations V001-V006 applied cleanly to a **bare** postgres:16 container
  (no compose init script) — matches the CI service-container setup exactly.
  See [[environment]] for the repro recipe.
- `db/tests/rls_smoke_test.sh` — all assertions pass against that same DB.
- `db/tests/fk_audit.sh` positive case — 53 uuid `*_id` columns checked, 15
  allowlisted, 0 violations, exit 0.
- **FK-audit negative case**: removed `kernel.outbox_events.aggregate_id`
  from the allowlist, gate correctly failed naming that exact column, exit 1.
  Restored the file, md5-verified byte-identical, `git status`/`git diff`
  clean.
- **Migration negative case**: scratch `V007` (never committed, built in
  `/private/tmp/.../scratchpad/migrations_test`, not in the repo) with a
  good table + a table FK'd to a nonexistent table — runner failed with
  `42P01`, rolled back **both** tables in that file, no `V007` row in
  `schema_migrations`. Transaction-per-file-on-failure behavior confirmed.
- Allowlist ↔ db/README.md "FK audit rules" cross-check: 1:1, no extras, no
  missing entries (verified two ways — the gate itself reports 0 violations,
  and I separately diffed the allowlist against the live `pg_catalog` column
  set to confirm no dead/stale allowlist entries either).
- `gh run view` on both the PR-cited run (28661105224) and the latest run
  (28661204871, from a follow-up push) — all 4 jobs green on both. The
  `pull_request` trigger with no branch filter does fire correctly for this
  stacked-branch PR (base = `feature/10-db-migrations`, not `main`) — this
  was itself one of the things to verify, not just trust.
- `permissions: contents: read` at workflow level, `concurrency` group with
  `cancel-in-progress: true` — appropriate for this size, no footgun.
- Only non-functional annotations in the Actions run: Node20→24 forced
  runtime (upstream `actions/checkout`/`setup-dotnet`/`setup-uv`, not this
  repo's problem) and a `setup-uv` cache-glob warning (no lockfile exists
  since `uvx` is used directly — harmless, cache never invalidates but also
  never matters at this size).

## Defect found (see [[fk-audit-gate-notes]] for full repro)
`db/tests/fk_audit.sh`: a failing `psql` query inside
`mapfile -t arr < <(psql_q ...)` is invisible to `set -euo pipefail` (process
substitution, not a pipe) — the script prints the psql error but then reports
"Checked 0 uuid *_id column(s)" and **exits 0**. Reproduced by pointing
`ADMIN_URL` at a wrong password. Low risk today (same hardcoded credentials
as the migration step just before it in the same CI job, so a real
connection failure would already have failed the job earlier), but it's the
exact silent-failure shape this gate exists to prevent, and it's a one-line
class of fix. Recommended before merge, but not a "the gate doesn't work"
finding — the gate works correctly on every path actually exercised by
current CI.

## Nits (non-blocking, do not gold-plate)
- Action versions (`actions/checkout@v4`, `setup-dotnet@v4`, `setup-uv@v5`)
  pinned to major tag, not SHA — normal practice at this stage, not worth
  raising further.
- `postgres:16` floating tag (not digest-pinned) — fine at this size per the
  task's own instruction not to demand gold-plating.
