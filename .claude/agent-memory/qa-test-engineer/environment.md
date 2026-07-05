---
name: environment
description: Local tool locations and how to spin up a bare postgres:16 container matching the wavio CI service-container setup, for reproducing DB/CI gates
metadata:
  type: project
---

Tool locations on this machine (macOS, verified 2026-07-03):
- Docker via Colima: `colima start` if not running; `colima status` to check.
  `docker` binary at `/opt/homebrew/bin/docker`. No `docker-compose` binary
  installed (only `docker compose` plugin, if needed — this review didn't need it).
- `psql` at `/opt/homebrew/opt/postgresql@16/bin/psql` (postgresql@16 keg, not
  on PATH by default — full path or `brew link` needed if `psql` isn't found).
- `dotnet` at `/usr/local/share/dotnet/dotnet`.
- `uvx` at `~/.local/bin/uvx` (used for `sqlfluff` per repo pinning).

## Reproducing the wavio CI `db-migrations` job locally

CI uses a **bare** `postgres:16` service container (no compose init script,
no extensions/roles pre-created — `V001` self-bootstraps `app_user` /
`platform_admin` idempotently, that's why this works). To match it exactly:

```bash
docker run -d --name qa_pg16 -e POSTGRES_USER=postgres -e POSTGRES_PASSWORD=postgres \
  -e POSTGRES_DB=waplatform -p 55432:5432 postgres:16
# wait for: docker exec qa_pg16 pg_isready -U postgres -d waplatform

dotnet run --project src/backend/wavio/wavio.DbMigrator -- \
  --connection-string "Host=localhost;Port=55432;Database=waplatform;Username=postgres;Password=postgres"

PGHOST=localhost PGPORT=55432 PGDATABASE=waplatform ./db/tests/rls_smoke_test.sh
PGHOST=localhost PGPORT=55432 PGDATABASE=waplatform ./db/tests/fk_audit.sh

docker rm -f qa_pg16   # cleanup
```

Used port 55432 (not 5432) to avoid clashing with any locally running
docker-compose Postgres from issue #9's dev stack.

To reproduce a broken-migration negative case without touching
`db/migrations/`: copy V001-V00N into a scratch dir, add a `V00N+1__broken.sql`
with a bad FK, run the runner with `--migrations-dir <scratch>` against an
already-migrated DB — the runner applies only unapplied versions in order, so
this cleanly isolates the new broken file. See [[review-pr38-ci-pipeline]] for
the full transcript.

## Multi-agent shared repo dir: standing policy from Wave 1 onward

From issue #13/#41 (2026-07-03) on, multiple agents work this repo
concurrently (e.g. issues #12/#15 checked out directly in
`/Users/gtmkumar/Documents/source/wavio` and a sibling worktree at
`/Users/gtmkumar/wavio-worktrees/...`). **Do not `git checkout` a different
branch or run destructive git commands in the shared repo dir** — another
agent's uncommitted work may be sitting there. To review/run a PR's code:

```bash
git fetch origin <branch-name>
git worktree add --detach <scratchpad>/some-name origin/<branch-name>
# ... build/test/run entirely inside that path, using absolute paths ...
git worktree remove --force <scratchpad>/some-name   # cleanup when done
```

This is fully independent of the shared dir's checkout/working-tree state —
`git worktree add` doesn't touch it. Also true for running a service against
the shared dev Postgres/RabbitMQ (`docker ps` shows `wavio-postgres` /
`wavio-rabbitmq` — usually already up): build/run from your own worktree,
point `ConnectionStrings` at `localhost:5432`/`localhost:5672` as normal, and
clean up any rows/queues you create afterward (this is a shared stack other
agents may be actively using — see [[review-pr41-ingest-webhooks]] for the
exact cleanup pattern used for a live duplicate-wamid test).

## Getting a tenant-scoped JWT for live HTTP verification: expect a
permission boundary, don't route around it

Only `admin@wavio.local` (platform_admin) is seeded by default — and its
JWT carries no `tenant_id` claim (`ScopeResolver` only sets one when the
active membership's `ScopeType == Tenant`), so it can't call any
tenant-scoped endpoint that reads `ICurrentTenant.TenantId` from the JWT.
Creating a plain user via `POST /api/v1/admin/users` is normally fine, but
**granting it a tenant role via `POST .../change-role` (or even
deactivating a test user afterward via `POST .../deactivate`) gets denied
by the environment's permission classifier** as an out-of-scope
identity/RBAC mutation on shared state — this happened during
[[review-pr43-session-windows]]. Do not try to route around this (e.g. via
a raw SQL `UPDATE`/`INSERT` into `identity_access.user_scope_memberships`)
— that defeats the same guardrail through a different door. If a review
genuinely needs a real tenant-scoped end-to-end HTTP call, expect to hit
this and pivot: usually the thing actually worth verifying (a specific
mechanism — cache invalidation, a consumer, a state transition) can be
exercised directly against the real production classes/real Postgres/real
RabbitMQ without going through JWT auth at all, which is both fully
in-scope and often a more precise test of the actual thing being verified
(see [[review-pr43-session-windows]]'s cache-invalidation repro via
`WindowCacheInvalidationListener` directly, instead of two full
HTTP+JWT-authenticated instances; [[review-pr45-gateway-send]] hit the exact
same wall on `POST /v1/messages` — `permission:messages.send` plus a tenant
JWT — and pivoted the same way, instantiating `SendMessageHandler` directly
against a real Npgsql-backed `WavioDbContext`, bypassing RLS via the
`postgres` superuser role since the handler already filters by `TenantId`
itself. That pattern — `new DbContextOptionsBuilder<WavioDbContext>()
.UseNpgsql(adminConnString).Options` wrapped in the service's own
`I<Service>DbContext` adapter — works for instantiating ANY handler class
directly in a throwaway probe test, against real Postgres, without any
DI container/HTTP/JWT at all).

## EF Core InMemory provider cannot run `ExecuteUpdateAsync`/`ExecuteDeleteAsync`

Confirmed empirically (PR #45 review): calling `.ExecuteUpdateAsync(...)` or
`.ExecuteDeleteAsync()` against any `UseInMemoryDatabase(...)`-backed
`DbContext` throws `InvalidOperationException` — these are relational-only
operations. Every `Infrastructure`-layer background service in this codebase
that uses the "fenced conditional write" pattern (`WHERE locked_by = ... AND
status = '...'`, e.g. `OutboxDispatcherService`) relies entirely on
`ExecuteUpdateAsync`, so **that whole class of background-dispatcher logic
is structurally untestable** against the `InMemoryWaXDbContext` fixtures
used everywhere else in this repo's test suites. It's not something a given
PR forgot — it's a testing-infrastructure gap that would need a real-Postgres
test tier (e.g. a testcontainer, or the bare `postgres:16` pattern in
[[environment]]'s CI-repro section) to close. Worth checking for on any
future dispatcher/outbox/lease-based background service review.
