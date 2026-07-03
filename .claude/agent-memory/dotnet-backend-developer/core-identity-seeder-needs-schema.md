---
name: core-identity-seeder-needs-schema
description: Why core.WebApi still crash-loops after issue #9's infra is up, and why that's expected until issue #10 lands
metadata:
  type: project
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
