---
name: issue-10-dotnet-wiring
description: What was built/changed on the .NET side of issue #10, and how it was verified
metadata:
  type: project
---

Issue #10 (2026-07-03), continued on `feature/10-db-migrations` (branch started by
the database-architect, who'd already pushed `db/migrations/V001-V006.sql`,
`db/tests/rls_smoke_test.sh`, `db/README.md`, `.sqlfluff`). Their handoff
(`.claude/agent-memory/database-architect/handoff.md` on that branch) specified the
.NET-side scope exactly; I followed it without deviation.

## What changed
1. **New project** `src/backend/wavio/wavio.DbMigrator` (console, raw Npgsql) —
   applies `db/migrations/V*.sql` in order, tracked in `public.schema_migrations`,
   Admin connection only. Added to `wavio.slnx`. Usage documented in `db/README.md`
   under "Migration runner (`wavio.DbMigrator`)". See [[decisions]] for the Npgsql
   `regclass` cast bug hit and fixed while building it.
2. **EF remaps** (both one-line `ToTable(...)` changes, per the architect's binding
   ruling): `TenantConfiguration.cs` → `tenancy.tenants`;
   `FeatureFlagConfiguration.cs` → `system.feature_flags`.
3. **RLS GUC rename**: `RlsConnectionInterceptor.cs` now sets `app.tenant_id`
   instead of `app.current_tenant_id` (spec §5 canonical spelling; SQL side already
   read both, so no migration was needed).
4. Three stale doc-comment fixes (no functional effect):
   `WavioDbContext.cs`, `Entities/TenancyOrg/Tenant.cs`, `AuditContext.cs`.

## How it was verified (all done live, not assumed)
1. Fresh DB: `docker compose -f docker-compose.dev.yml down -v && up -d --wait` →
   both containers healthy.
2. `dotnet run --project src/backend/wavio/wavio.DbMigrator` → applied V001-V006
   cleanly (`0 errors`); **re-ran it a second time** → all six reported "already
   applied, skipping" (idempotency confirmed, not just assumed from reading the SQL).
3. `db/tests/rls_smoke_test.sh` → all assertions pass, including both GUC spellings
   (`app.tenant_id` and `app.current_tenant_id`) and the append-only audit-log checks.
4. `dotnet build wavio.slnx` → 0 errors, 0 new warnings (135 pre-existing warnings,
   same count as before these changes).
5. Ran the Aspire AppHost in the background, polled every service's `/alive` with a
   bounded-timeout curl (see [[aspire-dcp-quirks]]): core (5050), wa-gateway-svc
   (5101), wa-ingest-svc (5102), wa-admin-svc (5103), wa-billing-svc (5104),
   wa-intel-svc (5105), and the gateway (8080) all returned `200`.
6. **Queried the database directly** (not just "no crash") to confirm the seeder
   actually wrote rows: `identity_access.permissions` = 15,
   `identity_access.roles` = 3, `identity_access.role_permissions` = 33,
   `tenancy.tenants` = 1, `identity_access.users` = 1
   (`admin@wavio.local`, `platform_admin`, `active`),
   `identity_access.user_scope_memberships` = 1. This is the strongest possible
   verification that both the EF remap and the schema are correctly aligned — a
   crash-free boot alone wouldn't have proven the seeder's queries hit the right
   tables.
7. Stopped everything afterward: AppHost + `dcp` + all spawned service processes
   (see [[aspire-dcp-quirks]] for why `pkill -f dcp` and the individual binaries
   are both needed, not just the `dotnet run --project wavio.AppHost` wrapper).
   Left the docker-compose stack (now with full schema) running/healthy for
   continued dev work.

## For whoever picks up issue #11 (CI) or #12 (VPS deploy)
- `wavio.DbMigrator` is the intended CI migration-apply step: fresh PG16 service
  container → `dotnet run --project src/backend/wavio/wavio.DbMigrator --
  --connection-string "..."` → `db/tests/rls_smoke_test.sh`. Both are idempotent
  and fast (~a few seconds for six files against an empty DB).
- The FK-audit gate and sqlfluff rules are the database-architect's scope
  (documented in `db/README.md`), not something I touched.
- On the VPS (`docker-compose.prod.yml`, issue #12), the same runner should be
  invoked post-deploy against the prod Admin connection before starting the
  services — nothing prod-specific needed, it's already environment-agnostic via
  `--connection-string` / `ConnectionStrings__Admin`.
