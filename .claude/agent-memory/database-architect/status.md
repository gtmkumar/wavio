---
name: status
description: Latest work status of the database-architect agent (issues #10 + #17, dated 2026-07-03)
metadata:
  type: project
---

# Status — 2026-07-03 (issue #17, Wave 1 — later the same day)

Branch `feature/17-wave1-migrations`, stacked on `feature/10-db-migrations`
(PR base = that branch; do not merge before PR #37):
`V007__messaging.sql`, `V008__sessions.sql`, `V009__templates.sql` + Wave 1
assertions in `db/tests/rls_smoke_test.sh` + db/README.md updates (ownership
map, outbound_outbox no-RLS rationale, idempotency mechanism, billing wamid
dependency note). Numbering ruling: issue #17's title says "V005–V008" but
those numbers were taken by identity_access/kernel — files are V007–V009.

Verified on fresh PG16 both via psql AND `dotnet run --project
src/backend/wavio/wavio.DbMigrator` (applied all 9); extended smoke test
25/25 passed; fk_audit.sh (fetched from feature/11-ci-pipeline into
scratchpad — it is NOT on this branch) passed with 84 columns, zero new
allowlist entries. Wave 1 rationale in [[decisions-issue-10]], service-dev
notes in [[handoff-issue-10]].

# Status — 2026-07-03 (issue #10)

Delivered on branch `feature/10-db-migrations` (do not merge to main until the
dotnet dev finishes the runner + entity remaps on the same branch):

- `db/migrations/V001__tenancy.sql` … `V006__kernel.sql` — six schemas
  (tenancy, waba, ingest, system, identity_access, kernel) + `app` helper
  schema + `public.schema_migrations` tracking table.
- `db/tests/rls_smoke_test.sh` — two-tenant RLS smoke test, exit≠0 on failure.
- `db/README.md` — migration convention, RLS pattern, schema ownership map,
  FK-audit allowlist, retention strategy.
- `.sqlfluff` (repo root) — postgres dialect; excludes RF04 (EF-mandated
  keyword column names) and PG01 (CONCURRENTLY pointless on fresh DBs).

**Why:** issue #10 (Wave 0) — first schemas of `waplatform`; core identity is
database-first and boots against this DDL.

**How to apply:** verified 2026-07-03 against real PostgreSQL 16 (compose
`down -v` → `up` → apply V001–V006 as postgres → smoke test → sqlfluff): all
green. Remaining work on the issue belongs to the dotnet dev — see
[[handoff-issue-10]]. Design rationale in [[decisions-issue-10]].
