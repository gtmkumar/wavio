---
name: issue-46-integration-tests
description: Issue #46 real-Postgres integration-test tier — what was built, fixture design, the two live-found seeding bugs, and how to run it locally on this Colima dev machine
metadata:
  type: project
---

# Issue #46 — real-Postgres integration-test tier (2026-07-06)

New project `src/backend/wavio/tests/WaPlatform.IntegrationTests` (added to `wavio.slnx` under
`/tests/`). xunit + Testcontainers.PostgreSql 4.13.0, postgres:16 pinned to the SAME digest as
`docker-compose.prod.yml` (issue #42). Never `EnsureCreated`/EF migrations — the fixture applies
`db/migrations/V001..V013` by invoking the real `wavio.DbMigrator` project via `dotnet run`
(the exact CLI shape `.github/workflows/ci.yml`'s `db-migrations` job already uses), never
reimplementing the apply-loop.

## Fixture design (`Support/DatabaseFixture.cs`)
- One container per TEST RUN (xunit `ICollectionFixture`, single `[CollectionDefinition
  ("IntegrationTests")]` — see `IntegrationTestCollectionDefinition.cs`). All 4 tests share it and
  run sequentially (xunit v2 default: collections parallelize with each other, tests *within* one
  collection do not) — this is what makes the isolation strategy below safe.
- **Isolation strategy: unique tenant/business-account/phone-number GUIDs per test, NOT ambient
  transactions, NOT TRUNCATE.** Deliberate: the dispatcher race test needs TWO independent
  DbContexts/connections to each see the OTHER's committed writes (an ambient per-test transaction
  would hide exactly the race under test); TRUNCATE would need FK-graph-aware ordering across
  schemas for no real benefit given tests already run sequentially with disjoint tenant data.
- Two connection strings exposed: `AdminConnectionString` (postgres superuser — seeding/global
  reads that must bypass RLS) and `AppConnectionString` (`app_user` — what every test drives its
  actual handler code through, so RLS is genuinely enforced, never bypassed).
- `RequiresDockerFactAttribute` (`Support/`) — a custom `FactAttribute` that probes the Docker
  socket (DOCKER_HOST, then `/var/run/docker.sock`, then this machine's Colima socket) and sets
  `Skip` at discovery time if none answer within 2s. No new package (xunit v2 has no `Skip.If`);
  reuses [[local-docker-backend-colima]]'s known path.

## Local run on THIS machine (Colima)
Testcontainers' Ryuk (resource-reaper) container fails to start under Colima's VZ-framework socket
with `invalid mount config for type "bind": stat .../docker.sock: operation not supported` — a
known Colima/Testcontainers interaction (Ryuk bind-mounts the host docker socket path into itself;
Colima's proxied socket isn't mountable the same way a native Linux socket is). Fix for local runs
only:
```
export DOCKER_HOST=unix://$HOME/.colima/default/docker.sock
export TESTCONTAINERS_RYUK_DISABLED=true
dotnet test src/backend/wavio/tests/WaPlatform.IntegrationTests/WaPlatform.IntegrationTests.csproj
```
Not needed in CI — GitHub-hosted `ubuntu-latest` runners have a native Docker Engine (real Linux,
no VM proxy), so Ryuk works there unmodified; `.github/workflows/ci.yml`'s new `integration-tests`
job does NOT disable Ryuk.

## Two live-found seeding bugs (not app bugs — test-fixture bugs, fixed before this was "done")
1. **Clock skew, Colima VM vs host**: seeding `outbound_outbox.next_attempt_at` via SQL `now()`
   made `OutboxDispatcherFencedWriteTests` intermittently fail `LeaseNextBatchAsync`'s
   `NextAttemptAt <= now` check — likely the container's own clock (inside Colima's Linux VM) vs.
   the test process's `DateTimeOffset.UtcNow` (macOS host). Fixed by stamping `next_attempt_at`/
   `accepted_at` from the TEST PROCESS's own clock (`UtcNow.AddSeconds(-10)`), matching the same
   clock the dispatcher code checks against — general lesson for any Testcontainers timestamp seed.
2. **Cross-tenant wamid race defeats the handler's own duplicate re-check via RLS**: first attempt
   at `RecordMessageCostIdempotencyTests` raced the SAME wamid across TWO DIFFERENT tenants (to
   dodge a `usage_counters` unique-constraint confound). This broke the handler itself: its
   `catch (DbUpdateException)` re-checks `MessageCosts.AnyAsync(wamid)` on the LOSING side's own
   RLS-scoped connection — which can never see the WINNING tenant's row (RLS), so `isDuplicate`
   comes back false and it re-throws. Not a bug in `RecordMessageCostCommandHandler` (a genuine
   wamid collision across tenants isn't a real scenario — one wamid always belongs to one tenant's
   conversation) — it's a test-design error. Fixed by keeping BOTH racing calls on the SAME tenant
   (the only realistic scenario) and pre-seeding both `usage_counters` rows ("utility" + "all") via
   raw SQL ahead of time, so the race is isolated to `message_costs_wamid_key` alone, not conflated
   with the unrelated `usage_counters_tenant_id_category_period_start_key` constraint.

## Test seam added to production code
`OutboxDispatcherService` (`WaGateway.Infrastructure/BackgroundWork/`): `LeaseNextBatchAsync` and
`ProcessEntryAsync` changed `private` → `internal`, plus a new `internal string InstanceId`
property, exposed to this test project via `<InternalsVisibleTo Include="WaPlatform.
IntegrationTests" />` in `WaGateway.Infrastructure.csproj` — same established convention as
`WaGateway.Application`/`WaAdmin.Infrastructure`/`WaIntel.Infrastructure`'s own
`InternalsVisibleTo` to their unit-test projects. No other production code changed.

## CI wiring
New `integration-tests` job in `.github/workflows/ci.yml`, parallel to (not nested in)
`build-test`/`db-migrations`. No `services: postgres` block — `DatabaseFixture` provisions its OWN
throwaway container via Testcontainers against the runner's native Docker daemon (GitHub-hosted
`ubuntu-latest` has one pre-installed). `build-test`'s existing `dotnet test "$SOLUTION"` step got
`--filter "FullyQualifiedName!~WaPlatform.IntegrationTests"` added so the two jobs don't both spin
up a container for the same tests.

## Coverage added (4 tests, each earning its runtime — not gold-plated)
1. `OutboxDispatcherFencedWriteTests` — the PR #45 headline gap (zero coverage because
   `ExecuteUpdateAsync` throws on EF InMemory). Drives the real internal methods via a
   pausable `ControllableGraphClient` fake to deterministically simulate lease-theft mid-flight.
2. `CreateTemplateCircularFkTests` — the PR #44 gap (two-step save has no regression net). FAILS
   with a real `23503` FK violation if reverted (verified live, see final report).
3. `TenantIsolationTests` — proves real RLS (FORCE ROW LEVEL SECURITY + `app_user` role), not an
   app-level filter, using a direct point-lookup-by-id with no tenant predicate in the LINQ at all.
4. `RecordMessageCostIdempotencyTests` — issue #19's `message_costs_wamid_key` defense-in-depth
   path, genuinely unreachable under `InMemoryWaBillingDbContext` (its own doc comment says so).

See also [[handoff]] and [[status]] for where this sits relative to #9-#42.
