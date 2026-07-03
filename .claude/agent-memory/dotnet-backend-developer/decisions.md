---
name: decisions
description: Design decisions made on the .NET side of issue #10 (migration runner, EF remaps, RLS GUC)
metadata:
  type: project
---

# Decisions — issue #10 .NET wiring (2026-07-03)

## Migration runner (`wavio.DbMigrator`)
- Custom console project, not DbUp — the whole job is "run N idempotent, self-
  registering .sql files in order," which doesn't need a migration framework's
  extra surface (journal tables, script providers, etc.). Raw `Npgsql` (10.0.3,
  matching what `Npgsql.EntityFrameworkCore.PostgreSQL` 10.0.2 already resolves
  elsewhere) is the only dependency.
- Each file is executed as **one multi-statement `NpgsqlCommand` wrapped in one
  transaction** — Npgsql's simple query protocol runs a whole `psql`-style script
  in one round trip, including `DO $$ ... $$` blocks (semicolons inside
  dollar-quoted bodies aren't statement separators). This mirrors what `psql -f`
  does, so "apply with plain psql" and "apply with the runner" stay equivalent
  (db/README.md documents both).
- Idempotency comes from each file's own trailing
  `INSERT INTO public.schema_migrations ... ON CONFLICT DO NOTHING` (the
  database-architect's design, see their `decisions.md`) — the runner just reads
  `public.schema_migrations` first and skips versions already present. Confirmed
  by running it twice against the same DB: second run reports "already applied"
  for all six and applies nothing.
- `public.schema_migrations` doesn't exist before V001 creates it. Naive
  `SELECT version FROM public.schema_migrations` would throw `42P01` on a fresh
  DB. Fixed by checking `to_regclass('public.schema_migrations')` first — but
  **cast it to `::text`**: Npgsql has no generic `object` mapping for the
  `regclass` type and throws `InvalidCastException` if you read it un-cast via
  `ExecuteScalarAsync<object>()`. Hit this once, fixed by adding `::text` to the
  query — worth remembering for any future ad hoc `regclass`/`regtype` reads via
  Npgsql.
- Migrations-directory auto-detection walks up from `AppContext.BaseDirectory`
  (stable regardless of caller's cwd) looking for `db/migrations`, since `db/` is
  a repo-root sibling of `src/`, not under the `.sln`'s directory. `--migrations-dir`
  / `--connection-string` args override for CI or non-default layouts.
- Project added to `wavio.slnx` as a top-level project (alongside
  `wavio.Utilities`, `wavio.SharedDataModel`) rather than folded into any service
  folder — it's a cross-cutting dev/ops tool, not part of a bounded context.

## EF remaps (per database-architect's binding handoff, no independent judgment call)
- `Tenant` → `tenancy.tenants` (was `tenancy_org.tenants`, a schema V001 never
  creates). `FeatureFlag` → `system.feature_flags` (was `kernel.feature_flags`,
  same reason — `kernel` schema only owns outbox/system_settings/file_attachments,
  see db/README.md's schema ownership map). Single-line `ToTable(...)` change
  each; column shapes were already exact per the architect's `decisions.md`, no
  other property/index changes needed.
- Also fixed three stale doc comments referencing `tenancy_org` /
  `app.current_tenant_id` (WavioDbContext.cs, Tenant.cs, AuditContext.cs) for
  accuracy — not a functional change, but leaving them wrong would mislead the
  next reader into thinking the old schema name is still live.

## RLS GUC spelling
- `RlsConnectionInterceptor` now sets `app.tenant_id` (spec §5 canonical name)
  instead of `app.current_tenant_id`. Minimal, single-line change — the SQL-side
  helper `app.current_tenant_id()` already reads both spellings (architect's
  design), so this was safe to change unilaterally without a migration. Did NOT
  touch `app.current_user_id` or `app.bypass_rls` — out of scope, handoff said
  "keep it minimal."
