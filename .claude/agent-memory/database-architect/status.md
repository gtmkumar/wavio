---
name: status
description: Latest work status of the database-architect agent (issue #10, dated 2026-07-03)
metadata:
  type: project
---

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
