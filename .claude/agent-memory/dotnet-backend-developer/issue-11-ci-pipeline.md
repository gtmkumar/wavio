---
name: issue-11-ci-pipeline
description: What was built for issue #11 (CI pipeline) and how each gate was verified locally before pushing
metadata:
  type: project
---

Issue #11 (2026-07-03), branch `feature/11-ci-pipeline` stacked on
`feature/10-db-migrations` (issue #10 wasn't on `main` yet, so this PR's base is
that branch, not `main` — GitHub auto-retargets to `main` once #37 merges and its
branch is deleted). Single workflow: `.github/workflows/ci.yml`, four jobs.

## Files
- `.github/workflows/ci.yml` — `build-test`, `sqlfluff`, `db-migrations`,
  `contracts-placeholder`. Triggers on `pull_request:` with **no branch filter**
  (deliberate — see below) and `push: branches: [main]`.
- `db/tests/fk_audit.sh` — the FK-audit gate script (bash + raw `pg_catalog`
  queries, no ORM). Reads `db/tests/fk_audit_allowlist.txt` for deliberate
  exclusions instead of hardcoding regex/table names in the script itself.
- `db/tests/fk_audit_allowlist.txt` — 15 entries, one per `schema.table.column`,
  transcribed 1:1 from db/README.md's "FK audit rules" section (polymorphic refs,
  trace/correlation ids, the cross-service outbox-consumer id). Keep these two in
  sync if either changes.

## Why the `pull_request:` trigger has no branch filter
The orchestrator's instruction was explicit: this repo currently stacks feature
branches (issue #11 depends on #10, which isn't merged to `main` yet), so a PR's
base branch is often not `main`. A `pull_request: branches: [main]` filter would
silently skip Actions runs on exactly this kind of PR. Confirmed this matters by
reading the GH Actions docs behavior, not just assuming — `branches:` under
`pull_request:` filters on the PR's **base**, not head, so it must be absent (or
include every stacked branch, which doesn't scale) for this workflow to fire on
`feature/11-ci-pipeline → feature/10-db-migrations`.

## FK-audit gate design
- Query 1: every `pg_attribute` column named `%_id`, type `uuid`, on a table with
  `relispartition = false` (excludes partition children entirely — they clone the
  parent's FK per Postgres declarative partitioning, so auditing them too would be
  redundant, and db/README.md says "audit parents only").
- Query 2: every column that's part of *some* `pg_constraint` with `contype = 'f'`
  (via `unnest(conkey)`), regardless of partition status (harmless — irrelevant
  since Query 1 already excludes partitions from the columns being checked).
- Violation = in Query 1, not in Query 2, not in the allowlist file.
- Verified 53 uuid `*_id` columns total post-V001–V006, 15 correctly allowlisted,
  0 violations — matches db/README.md's documented exclusions exactly (counted by
  hand against the migrations before writing the allowlist, then confirmed the
  script's own count matched).

## Verification performed (mandatory — negative cases included, nothing broken committed)
1. **sqlfluff**: `uvx sqlfluff==4.2.2 lint db/migrations/` → clean, locally, before
   pinning that exact version in the workflow.
2. **FK-audit positive**: ran `db/tests/fk_audit.sh` against the fully-migrated DB
   → 0 violations.
3. **FK-audit negative**: temporarily removed one real entry
   (`kernel.outbox_events.aggregate_id`) from the allowlist file in place, re-ran
   the script → it correctly reported that exact column as a violation and exited
   1. Restored the file immediately after (`diff` confirmed byte-identical to the
      original) — nothing broken was committed.
4. **Migration-validation positive**: fresh bare `postgres:16` container (no
   compose init script — deliberately matching what the CI service container looks
   like, unlike `docker-compose.dev.yml` which also runs
   `deploy/postgres/init/001-create-app-role.sql`) → `wavio.DbMigrator` applied
   V001–V006 cleanly. Confirms V001's idempotent `app_user`/`platform_admin`
   bootstrap (issue #10's design) is what makes CI work without the compose init
   script — the CI job needs no separate role-bootstrap step.
5. **Migration-validation negative**: copied `db/migrations/` to `/tmp`, added a
   throwaway `V007__broken_test.sql` (one good `CREATE TABLE`, one with an FK to a
   nonexistent table) referencing a nonexistent table, ran the runner with
   `--migrations-dir /tmp/...` against the live DB → failed with the expected
   `42P01` error, and confirmed via direct query that **both** tables from the
   broken file were rolled back (transaction-per-file works) and `V007` never
   appears in `public.schema_migrations`. Deleted the scratch directory
   afterward — nothing broken was ever written under `db/migrations/`.
6. **build-test job**: `dotnet build "$SOLUTION" --configuration Release` then
   `dotnet test "$SOLUTION" --configuration Release --no-build` locally — 0
   errors, exit 0. No test projects exist yet (Wave 0); `dotnet test` against a
   solution with zero test projects still exits 0 with no special flags needed —
   confirmed this locally rather than assuming, since a nonzero exit here would
   have silently broken the "must still run green" requirement.
7. **contracts-placeholder job**: `dotnet build
   src/backend/wavio/WaPlatform.Contracts/WaPlatform.Contracts.csproj
   --configuration Release` locally — 0 errors.
8. Full end-to-end dry run of all three `db-migrations` job steps in the exact
   order and with the exact env vars the workflow uses, against the bare
   container from point 4 above (not the compose stack) — all green.
9. Pushed and opened PR #38 (base `feature/10-db-migrations`) — real GitHub
   Actions run triggered immediately (confirming the no-branch-filter
   `pull_request:` trigger actually fires on a non-`main`-base PR) and all four
   jobs passed: https://github.com/gtmkumar/wavio/actions/runs/28661105224

## QA-found bug, fixed 2026-07-03 (post-review, same day)
QA (independent adversarial reproduction, not just re-reading the diff) found a
real bug in `fk_audit.sh`: `mapfile -t arr < <(psql_q ...)` used process
substitution, whose exit status `set -e`/`pipefail` **cannot see** — bash only
traps failures in the direct command list, not inside `<(...)`. With a bad
`ADMIN_URL` password, `psql` printed an auth error to stderr but `mapfile` still
"succeeded" (reading zero bytes), so the gate reported "Checked 0 uuid *_id
column(s)" and **exited 0** — the exact silent-pass failure mode this gate exists
to prevent (147-missing-FKs lesson).

Fix: capture query output into a variable first (`raw="$(psql_q "$q")" || exit
1`), which *does* propagate the command-substitution's exit status, then
`mapfile -t arr <<< "$raw"` from the variable — guarding `[[ -n "$raw" ]]` first
since `mapfile <<< ""` would otherwise create a one-element array holding an
empty string instead of a zero-length array. Wrapped in a `psql_lines
<array-name> <query>` helper (uses `local -n` nameref) so both queries share the
fix instead of duplicating it.

Verified all three required cases after the fix, same live DB:
1. Bad password (`ADMIN_URL` with wrong password) → now exits 1 with a clear
   `[fk_audit] psql query failed — aborting` message, instead of silently
   reporting 0 checked / exit 0.
2. Good connection → byte-identical behavior to before the fix: 53 checked, 15
   allowlisted, 0 violations, exit 0.
3. Removed-allowlist-entry negative case (same procedure as before: pull
   `kernel.outbox_events.aggregate_id` out of the allowlist in place, restore
   after) → still correctly fails naming that exact column.

Pushed as a follow-up commit on `feature/11-ci-pipeline`; new Actions run on PR
#38 green again: https://github.com/gtmkumar/wavio/actions/runs/28661908138.

**Takeaway for future bash gate scripts**: never rely on `set -e`/`pipefail` to
catch a failure inside `<(process substitution)` used as a read source —
capture to a variable with `x="$(cmd)" || handle_failure` instead, and only
`mapfile`/split the variable once you know the command actually succeeded.

## Local tooling note
This machine's default `/bin/bash` is 3.2 (no `mapfile`, no `declare -A`) — had to
`brew install bash` (5.3.15) to run `fk_audit.sh` locally at all. GitHub Actions
`ubuntu-latest` ships a modern bash by default, so the script itself targets that,
not macOS's ancient one; invoke it locally via `/opt/homebrew/bin/bash
db/tests/fk_audit.sh` (or just `./db/tests/fk_audit.sh` once a modern bash is
first on `$PATH`).

## For whoever touches this next
- If you add a new migration file, no CI change is needed — the runner picks up
  `V*.sql` by glob.
- If you add a genuinely FK-less `*_id` uuid column, add it to
  `db/tests/fk_audit_allowlist.txt` **and** to db/README.md's "FK audit rules" in
  the same PR, or the gate will fail (correctly).
- If/when real xUnit test projects land, `dotnet test` in `build-test` needs no
  changes — it already runs against the whole solution.
- `contracts-placeholder`'s "Contract tests placeholder" step should be replaced
  with real consumer-driven tests in Wave 1, not appended to.
