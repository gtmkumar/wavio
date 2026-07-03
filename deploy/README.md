# Wavio — local dev infrastructure

Brings up the two stateful dependencies every Wavio service needs locally:
PostgreSQL 16 and RabbitMQ. Everything else (the 5 wa-* services, core identity,
the YARP gateway) runs via the .NET Aspire AppHost, **not** this compose file —
Aspire is local-dev orchestration only; it injects connection strings that point
at the containers below (see `src/backend/wavio/wavio.AppHost/AppHost.cs`).

## Quick start

```bash
# From the repo root:
docker compose -f docker-compose.dev.yml up -d --wait

# Then run the app stack:
cd src/backend/wavio
ASPNETCORE_ENVIRONMENT=Development dotnet run --project wavio.AppHost
```

Schema migrations (V001+, issue #10) are not part of this bootstrap — until they're
applied, `waplatform` is an empty database. Anything that queries application tables
before then (e.g. core identity's dev seeder) will fail to boot; that's expected and
unblocked by issue #10, not a defect here.

## What's included

| Service | Image | Port(s) | Credentials |
|---|---|---|---|
| PostgreSQL 16 | `postgres:16` | 5432 | `postgres` / `postgres` (superuser, dev seeding only) |
| RabbitMQ (+ management UI) | `rabbitmq:3-management` | 5672 (AMQP), 15672 (UI) | `guest` / `guest` |

On first start, `deploy/postgres/init/001-create-app-role.sql` runs automatically and:
- creates the `waplatform` database (via `POSTGRES_DB`, before init scripts run)
- creates a non-superuser `app_user` role (`LOGIN`, password `app_user`, `NOBYPASSRLS`)
  — this is the role every service actually connects as (`ConnectionStrings:Default`);
  RLS enforcement (spec §5, `app.tenant_id` GUC) depends on it never being superuser.

Postgres and RabbitMQ data persist in named Docker volumes (`wavio-postgres-data`,
`wavio-rabbitmq-data`) across `docker compose down` / restarts. To fully reset:

```bash
docker compose -f docker-compose.dev.yml down -v
```

## Connection strings (match AppHost defaults)

```
ConnectionStrings__Default  = Host=localhost;Port=5432;Database=waplatform;Username=app_user;Password=app_user
ConnectionStrings__Admin    = Host=localhost;Port=5432;Database=waplatform;Username=postgres;Password=postgres
ConnectionStrings__RabbitMq = amqp://guest:guest@localhost:5672
```

`Admin` is for Development-only seeding (bypasses RLS as the Postgres superuser) —
never used at runtime by request-handling code paths.

## Verifying the stack manually

```bash
# Postgres reachable as app_user (RLS-enforced role):
psql "postgresql://app_user:app_user@localhost:5432/waplatform" -c '\conninfo'

# RabbitMQ management UI:
curl -u guest:guest http://localhost:15672/api/overview
```

## Production counterpart

`docker-compose.prod.yml` (issue #12) runs the same Postgres/RabbitMQ services plus
the 5 wa-* services and Caddy (TLS) on the VPS — see
`docs/BUILD_PLAN.md` → "Runtime: Docker Compose on the VPS".
