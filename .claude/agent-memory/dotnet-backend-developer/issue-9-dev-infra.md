---
name: issue-9-dev-infra
description: What was built for GitHub issue #9 (local dev infra) and where the files live
metadata:
  type: project
---

Issue #9 (2026-07-03, branch `feature/9-dev-infra`) added local dev infra: PostgreSQL 16
+ RabbitMQ via Docker Compose, with `waplatform` DB + `app_user` role bootstrap.

Files:
- `docker-compose.dev.yml` (repo root) — postgres:16 + rabbitmq:3-management, named
  volumes `wavio-postgres-data` / `wavio-rabbitmq-data`, healthchecks so
  `docker compose up -d --wait` blocks until both are actually ready.
- `deploy/postgres/init/001-create-app-role.sql` — runs once via
  `docker-entrypoint-initdb.d` on first container init (i.e. only when the postgres
  volume is empty — `docker compose down -v` to force re-bootstrap). Creates
  `app_user` (LOGIN, NOSUPERUSER NOCREATEDB NOCREATEROLE NOBYPASSRLS) and grants
  CONNECT + schema `public` USAGE. The `waplatform` database itself is created by the
  official postgres image from `POSTGRES_DB=waplatform`, before init scripts run.
- `deploy/README.md` — quick-start + connection-string reference.

Placement decision: root-level `docker-compose.dev.yml` was chosen (not `infra/` or
`deploy/docker-compose.yml`) to mirror the future `docker-compose.prod.yml` from
issue #12 (docs/BUILD_PLAN.md names both at that implied root level). Postgres init
scripts and docs went under `deploy/` since issue #12's Caddy/VPS config will likely
land there too.

Scope boundary (deliberate, per the issue): this does NOT create any schemas/tables —
those are versioned SQL migrations V001+ in issue #10. See
[[core-identity-seeder-needs-schema]] for the consequence of that boundary.

Connection strings match what `wavio.AppHost/AppHost.cs` already had hardcoded as
fallbacks (`ConnectionStrings:Default` = app_user/app_user, `:Admin` = postgres/postgres,
`:RabbitMq` = guest/guest@localhost:5672) — no AppHost changes were needed, only the
infra to back those strings.
