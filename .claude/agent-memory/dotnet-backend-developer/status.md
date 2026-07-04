---
name: status
description: Current status of the dotnet-backend-developer's work across issues #9, #10, #11
metadata:
  type: project
---

**2026-07-03 — issue #11 (CI pipeline) done, PR open on `feature/11-ci-pipeline`
(stacked on `feature/10-db-migrations`, since #10 isn't merged to `main` yet).**

Wired up `.github/workflows/ci.yml`: build/test, sqlfluff lint, migration
validation (fresh Postgres service container → `wavio.DbMigrator` → RLS smoke test
→ FK-audit gate), and a WaPlatform.Contracts placeholder job. Every gate was
exercised locally, including the negative cases (a deliberately broken migration,
a deliberately un-allowlisted FK-less column), before pushing. See
[[issue-11-ci-pipeline]] for the full verification log and design notes.

**2026-07-03 — issue #10 (.NET half) done, PR #37 open on `feature/10-db-migrations`
(reviewed/approved by the orchestrator; merge blocked on a permissions issue on
their side, not a defect).**

Continued on the database-architect's branch (they'd already pushed
`db/migrations/V001-V006.sql` + `db/tests/rls_smoke_test.sh` + `db/README.md`,
verified against fresh Postgres). My scope was the .NET side: a migration runner,
three small EF remaps, and full end-to-end verification. See
[[issue-10-dotnet-wiring]] for what changed and [[core-identity-seeder-needs-schema]]
for confirmation that the long-standing core.WebApi crash-loop (tracked since issue
#9) is now resolved.

Previously: issue #9 (dev infra — docker-compose Postgres/RabbitMQ, `app_user`
bootstrap) shipped and merged as PR #36. See [[issue-9-dev-infra]].

Next expected work in this area: issue #12 (VPS deploy) will want
`wavio.DbMigrator` run against the prod compose stack post-deploy — same runner,
no changes needed, per [[handoff]].
