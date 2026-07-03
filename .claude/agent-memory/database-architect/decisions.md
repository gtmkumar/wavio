---
name: decisions-issue-10
description: Design decisions and EF-derived DDL surprises for the V001-V006 migrations
metadata:
  type: project
---

# Design decisions — issue #10 migrations (2026-07-03)

## RLS pattern
- Helper `app.current_tenant_id()` reads **both** GUC spellings:
  `app.tenant_id` (spec §5) with fallback to `app.current_tenant_id` — the
  shared EF `RlsConnectionInterceptor` (SharedDataModel/Persistence/
  Interceptors) sets `app.current_tenant_id`, NOT the spec name. Reconciled in
  SQL so no C# change is required.
- The interceptor's `app.bypass_rls` GUC is **ignored** by every policy: a
  client-settable GUC is not a security boundary. Bypass = membership in
  `platform_admin` (NOLOGIN, NOBYPASSRLS) via `app.is_platform_admin()`
  (`pg_has_role`) inside each policy — works under FORCE RLS, auditable.
- Three policy shapes, all named `tenant_isolation`:
  1. strict: `tenant_id = app.current_tenant_id() OR app.is_platform_admin()`
  2. tenants table: scopes on `id`
  3. nullable-tenant: adds `tenant_id IS NULL OR …` (roles, feature_flags,
     system_settings, file_attachments, both audit tables). Single FOR ALL
     policy on purpose: split read/write policies would break EF updates of
     global rows (e.g. feature-flag evaluation counters) with 0-row
     concurrency exceptions.
- Deliberately NO RLS: ingest.* (pre-tenant-resolution), system.jobs/job_runs
  and kernel.outbox_* (background workers have no tenant ctx), identity users/
  permissions/tokens (platform-global users; tenancy via
  user_scope_memberships).

## TTL / partitioning
- `ingest.raw_webhooks`: weekly RANGE partitions on received_at +
  `ingest.maintain_raw_webhooks(retain_days, weeks_ahead)` (creates ahead,
  DROPs expired — O(1), no vacuum churn). Effective TTL 30–37 days. DEFAULT
  partition as safety net. Chosen over pg_cron/pg_partman (OSS-only, no
  extra extensions) and over DELETE jobs (bloat).
- Both audit tables: monthly partitions via generic
  `app.ensure_month_partitions(regclass, months_ahead)`; composite PK
  `(id, occurred_at)` — REQUIRED by AuditLogConfiguration ("Composite PK
  required by PG range partitioning").
- Append-only enforcement = grants (SELECT, INSERT only, even for
  platform_admin); partitions get no direct grants so nobody can DML them
  directly.

## EF-derived DDL surprises (identity_access / kernel / tenants)
- `users.email` is **citext** → `CREATE EXTENSION citext` in V001.
- Singular table names mandated by EF: `login_history`,
  `user_permission_override`.
- `user_permission_override.id` is ValueGeneratedNever (app supplies id);
  natural key enforced by expression index
  `(user_id, permission_id, coalesce(scope_type,''), coalesce(scope_id, zero-uuid))`.
- `refresh_tokens.family_id` is NOT NULL self-FK (first token references
  itself); parent_token_id also self-FK.
- audit_logs has an actor_user_id column with NO EF FK mapping — I added the
  DB FK anyway (SET NULL); extra DB-side FKs are invisible to EF at runtime.
- pan_number/bank_account_number/upi_id are AES-GCM ciphertext → text, no
  length limits.
- users email/phone unique indexes made **partial** (`WHERE deleted_at IS
  NULL`) so soft-deleted accounts don't block re-registration; tenants.code
  kept plain-unique (codes must never be reused). EF never validates index
  shape.

## Other choices
- `NULLS NOT DISTINCT` (PG15+) on unique indexes containing nullable tenant_id
  / scope_id (roles code, feature flags key, system_settings key,
  user_scope_memberships) — plain unique would allow duplicate "global" rows.
- External Meta ids stored as varchar `meta_waba_id` / `meta_phone_number_id`;
  FK-audit gate rule defined as "uuid *_id only" so these are exempt (full
  allowlist in db/README.md).
- No CHECK constraints on EF-owned free-form status columns (unknown seeder
  values would break boot); CHECKs only where the platform owns semantics
  (phone_numbers status state machine, api_keys scope, job_runs status, …).
- Each migration self-registers in `public.schema_migrations`.
- V001 idempotently creates app_user/platform_admin roles (CI runs on bare PG
  without the compose init script).
- system.audit_log.actor_user_id FK is added in V005 (users doesn't exist in
  V004) — remember this ordering trick for future cross-schema FKs.
