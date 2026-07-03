---
name: handoff
description: Handoff notes for whoever picks up work after issues #10 and #11 (.NET side)
metadata:
  type: project
---

# Handoff — after issues #10 and #11 (2026-07-03)

Full detail in [[issue-10-dotnet-wiring]], [[issue-11-ci-pipeline]], and
[[decisions]]; this is the short version for whoever picks up next.

## State of the world
- `feature/10-db-migrations` has a PR open (not merged) closing #10: DB migrations
  V001-V006 (database-architect) + `wavio.DbMigrator` runner, EF remaps, RLS GUC
  rename (dotnet-backend-developer). Verified end-to-end — see
  [[issue-10-dotnet-wiring]] for the exact verification steps and DB row counts.
  Reviewed/approved by the orchestrator; merge itself is blocked on a permissions
  issue on their side, not a defect in the work — don't touch that PR.
- `feature/11-ci-pipeline` (stacked on top of `feature/10-db-migrations`) has a PR
  open closing #11: `.github/workflows/ci.yml` with build-test, sqlfluff,
  migration-validation (+ RLS smoke test + FK-audit gate in the same job), and a
  WaPlatform.Contracts placeholder job. See [[issue-11-ci-pipeline]] for exactly
  how each gate — including the negative cases — was verified locally before
  pushing.
- `core.WebApi` now boots and seeds successfully under the Aspire AppHost. The
  long-running crash-loop tracked since issue #9 is resolved — see
  [[core-identity-seeder-needs-schema]] (kept for historical "why", marked
  RESOLVED at the top).
- `docker-compose.dev.yml` (issue #9) + `db/migrations` (issue #10) together give
  a fully working local dev stack: `docker compose up -d --wait` →
  `dotnet run --project src/backend/wavio/wavio.DbMigrator` → AppHost boots green.
  CI (issue #11) exercises the same runner against a bare `postgres:16` service
  container with no compose init script — works because V001 self-bootstraps
  `app_user`/`platform_admin` idempotently (the database-architect's design).

## Immediate next steps (not done by me, out of scope for #10/#11)
- **Issue #12 (VPS deploy)**: `docker-compose.prod.yml` will need the same
  migrate-then-start sequence; `wavio.DbMigrator` takes `--connection-string` so
  this needs no prod-specific code, just a deploy-script step.
- When #10 merges to `main` and its branch is deleted, GitHub auto-retargets the
  #11 PR's base to `main` — no action needed, but don't be surprised by the diff
  suddenly including #10's commits too until that merge happens.

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
