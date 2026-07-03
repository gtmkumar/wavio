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

## Production

`docker-compose.prod.yml` (issue #12) runs the full platform on a single VPS —
core identity, the 5 wa-* services, PostgreSQL 16, RabbitMQ, and Caddy as the
TLS-terminating reverse proxy. Free/OSS only, no managed cloud services (see
`docs/BUILD_PLAN.md` → "Runtime: Docker Compose on the VPS"). Only Caddy
publishes host ports (80/443) — every other service is reachable solely over
the internal compose network; `deploy/vps/setup-ufw.sh` is defense in depth on
top of that.

### One-time VPS setup

```bash
# 1. Firewall baseline (only 80/443/SSH reachable) — see that script's header
#    for why you should do this over a console session, not cold over SSH.
sudo ./deploy/vps/setup-ufw.sh

# 2. Clone the repo to /opt/wavio (docker-compose.prod.yml assumes this is the
#    compose working directory — relative paths like ./deploy/... resolve
#    against it).
git clone <repo-url> /opt/wavio && cd /opt/wavio

# 3. Get the real age private key onto the box (out-of-band — scp/paste, never
#    via git) and decrypt the prod secrets — see deploy/secrets/README.md for
#    the full secrets workflow (encrypting, rotating, what each key is for).
export SOPS_AGE_KEY_FILE=/root/.config/sops/age/keys.txt
./deploy/secrets/decrypt.sh deploy/secrets/prod.env.enc /opt/wavio/.env
./deploy/secrets/decrypt.sh deploy/secrets/jwt-private-key.pem.enc \
    /opt/wavio/secrets/jwt-private-key.pem
```

### First deploy / any schema change

```bash
docker compose -f docker-compose.prod.yml pull
docker compose -f docker-compose.prod.yml run --rm migrator   # applies db/migrations/V*.sql
docker compose -f docker-compose.prod.yml up -d --remove-orphans
```

`.github/workflows/deploy.yml` builds + pushes every service image to GHCR on
every push to `main`; the VPS rollout itself is a manual `workflow_dispatch`
(needs `VPS_HOST`/`VPS_USER`/`VPS_SSH_KEY` repo secrets configured first — none
of that exists yet, this is the user-side part) that runs the same three
commands above over SSH.

### What was verified locally (no VPS/domain available in this environment)

- All 8 images (7 services + `wavio.DbMigrator`) build clean from the shared
  `Dockerfile` and boot correctly under `docker-compose.prod.yml`'s exact
  environment wiring (verified as `ASPNETCORE_ENVIRONMENT=Production`).
- The full dependency chain (postgres → migrator → core → wa-*-svc →
  wavio-gateway) starts and reports healthy end-to-end with locally-tagged
  images standing in for GHCR ones.
- `deploy/postgres/init-prod/001-create-app-role.sh` creates `app_user` with
  the real prod password *before* `wavio.DbMigrator` runs, so V001's own
  (idempotent, dev-only-strength) bootstrap correctly skips it.
- Caddy: config validated with the real `caddy` binary, and a full reverse-proxy
  smoke test was run with `tls internal` (self-signed) proxying to the actual
  `wavio.Gateway` image — confirmed `server: Kestrel` + `via: 1.1 Caddy` in the
  response, i.e. real end-to-end TLS-terminate-then-proxy, not just config
  parsing. A real Let's Encrypt cert needs `WAVIO_DOMAIN` to resolve to a real
  public VPS IP, which this environment doesn't have — that part is genuinely
  user-side.
- `deploy/db-backup/pg_dump_nightly.sh` and `restore_drill.sh`: both run for
  real against a live database (dump → restore into a scratch DB → row-count
  verification across every migrated schema).
- SOPS/age tooling: full encrypt → commit-shape → decrypt → diff round-trip
  with a throwaway (never committed) age key, for both the dotenv file and a
  binary PEM; also confirmed decryption fails loudly (nonzero exit, no
  half-written output file) without the right key.
- `deploy/vps/setup-ufw.sh`: actually run (in a privileged container, not just
  read) — confirmed the exact final ruleset matches the intended baseline.

See `.claude/agent-memory/dotnet-backend-developer/issue-12-vps-deploy.md` for
the exact commands run for each of the above, plus two real bugs this
verification caught (a Docker `ENTRYPOINT` that silently swallowed CLI args,
and a `deploy/backup/` directory name that macOS's case-insensitive filesystem
made an existing `.gitignore` rule silently swallow).
