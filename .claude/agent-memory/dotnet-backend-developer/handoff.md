---
name: handoff
description: Handoff notes for whoever picks up work after issue #10 (.NET side)
metadata:
  type: project
---

# Handoff — after issue #10 (2026-07-03)

Full detail in [[issue-10-dotnet-wiring]] and [[decisions]]; this is the short
version for whoever picks up next.

## State of the world
- `feature/10-db-migrations` has a PR open (not merged) closing #10: DB migrations
  V001-V006 (database-architect) + `wavio.DbMigrator` runner, EF remaps, RLS GUC
  rename (dotnet-backend-developer). Verified end-to-end — see
  [[issue-10-dotnet-wiring]] for the exact verification steps and DB row counts.
- `core.WebApi` now boots and seeds successfully under the Aspire AppHost. The
  long-running crash-loop tracked since issue #9 is resolved — see
  [[core-identity-seeder-needs-schema]] (kept for historical "why", marked
  RESOLVED at the top).
- `docker-compose.dev.yml` (issue #9) + `db/migrations` (issue #10) together give
  a fully working local dev stack: `docker compose up -d --wait` →
  `dotnet run --project src/backend/wavio/wavio.DbMigrator` → AppHost boots green.

## Immediate next steps (not done by me, out of scope for #10)
- **Issue #11 (CI)**: wire `wavio.DbMigrator` + `db/tests/rls_smoke_test.sh` +
  `sqlfluff lint db/migrations/` + the FK-audit gate into GitHub Actions against a
  real Postgres 16 service container. The runner and smoke test are already
  CI-ready (tested twice locally, fully idempotent, no interactive prompts).
- **Issue #12 (VPS deploy)**: `docker-compose.prod.yml` will need the same
  migrate-then-start sequence; `wavio.DbMigrator` takes `--connection-string` so
  this needs no prod-specific code, just a deploy-script step.

## Watch out for
- If you touch `RlsConnectionInterceptor.cs` again: the GUC name is now
  `app.tenant_id` (not `app.current_tenant_id`). The SQL-side helper
  `app.current_tenant_id()` reads both spellings as a fallback, so don't "fix"
  what looks like a naming mismatch there — it's deliberate back-compat, per the
  database-architect's design.
- If you add new EF entities/tables: the DDL is canonical in `db/migrations/`, not
  in the EF model (database-first) — coordinate schema changes with a new
  `V00N__*.sql` file, not just an EF configuration edit. See db/README.md's
  "Schema ownership map" for which schema owns what.
- Aspire/Colima/dcp environment quirks (starting Docker, probing ports, killing
  the AppHost cleanly) are documented in [[local-docker-backend-colima]] and
  [[aspire-dcp-quirks]] — still accurate, used again successfully during #10.
