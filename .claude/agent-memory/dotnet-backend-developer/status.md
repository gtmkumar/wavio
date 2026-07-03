---
name: status
description: Current status of the dotnet-backend-developer's work across issues #9 and #10
metadata:
  type: project
---

**2026-07-03 — issue #10 (.NET half) done, PR open on `feature/10-db-migrations`.**

Continued on the database-architect's branch (they'd already pushed
`db/migrations/V001-V006.sql` + `db/tests/rls_smoke_test.sh` + `db/README.md`,
verified against fresh Postgres). My scope was the .NET side: a migration runner,
three small EF remaps, and full end-to-end verification. See
[[issue-10-dotnet-wiring]] for what changed and [[core-identity-seeder-needs-schema]]
for confirmation that the long-standing core.WebApi crash-loop (tracked since issue
#9) is now resolved.

Previously: issue #9 (dev infra — docker-compose Postgres/RabbitMQ, `app_user`
bootstrap) shipped and merged as PR #36. See [[issue-9-dev-infra]].

Next expected work in this area: issue #11 (CI — sqlfluff, FK-audit gate, migration
validation against real Postgres) will likely want to reuse `wavio.DbMigrator` as
the CI migration-apply step; issue #12 (VPS deploy) will want the same runner run
against the prod compose stack post-deploy.
