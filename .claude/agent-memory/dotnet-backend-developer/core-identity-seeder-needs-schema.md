---
name: core-identity-seeder-needs-schema
description: Why core.WebApi crash-looped between issues #9 and #10, and how it was confirmed resolved
metadata:
  type: project
---

**RESOLVED 2026-07-03 (issue #10).** Once `db/migrations/V001-V006` were applied via
`wavio.DbMigrator`, `core.WebApi` booted clean and `IdentitySeeder` populated real
rows (15 permissions, 3 roles, 33 role_permissions, 1 tenant, 1 `admin@wavio.local`
platform_admin user) — verified by querying `tenancy.tenants` /
`identity_access.users` directly after a full AppHost run. See [[issue-10-dotnet-wiring]]
for what changed. The history below is kept for context on *why* this was expected,
not because it's still an open issue.

---

`core.WebApi/Program.cs` runs `IdentitySeeder` synchronously in Development, before
`app.Run()` (core.Infrastructure/Seeders/IdentitySeeder.cs). It queries
`identity_access.permissions`, `tenancy_org.tenants`, etc. via `WavioDbContext`, which
is database-first and expects those schemas/tables to already exist — there is no
`Database.Migrate()` / `EnsureCreated()` call anywhere in the codebase (checked
2026-07-03). Those schemas are created by the versioned SQL migrations in issue #10
(V001–V004 + RLS), which is explicitly out of scope for issue #9.

**Consequence:** once issue #9's docker-compose stack (Postgres reachable, `app_user`
role valid, `waplatform` DB exists) is up and you `dotnet run` the AppHost, `core`
still fails to boot — but the failure mode changes from "connection refused" (no
Postgres at all) to `Npgsql.PostgresException 42P01: relation
"identity_access.permissions" does not exist` (Postgres reachable, schema missing).
Confirmed by running `core.WebApi` standalone on 2026-07-03: it authenticates and
connects fine, then throws that exact exception out of `SeedPermissionsAsync`.

This is the correct, expected state after #9 alone — **not a defect** in the dev
infra. Don't try to "fix" it by adding ad hoc EF migrations/EnsureCreated inside an
issue-#9-scoped PR; the fix is issue #10 landing.

The 5 wa-* services (WaGateway/WaIngest/WaAdmin/WaBilling/WaIntel) and the YARP
gateway do **not** eagerly touch the DB at startup (no seeders wired into their
`Program.cs`), so they boot and report `/alive` healthy even with zero schema present.
Only `core` is blocked. See [[issue-9-dev-infra]].
