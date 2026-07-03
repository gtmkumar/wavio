# Wavio database — migrations, RLS, conventions

Single PostgreSQL 16 instance, database **`waplatform`**, DDD schema split
(spec §6). **The versioned SQL files in `db/migrations/` are the canonical
schema** — never redefine tables anywhere else (markdown, EF model, etc.).

## Migration convention

- Files: `db/migrations/V00N__<schema>.sql`, forward-only, applied strictly in
  order. No down-migrations — fix forward with a new version.
- Tracking: `public.schema_migrations (version, applied_at)`. Each file
  self-registers its version at the bottom (`ON CONFLICT DO NOTHING`), so plain
  `psql -f` application stays consistent. The .NET migration runner
  (`wavio.DbMigrator`, below):
  1. connects with the **Admin** (superuser) connection string, not `app_user`
     (migrations create schemas/roles/policies);
  2. `SELECT version FROM public.schema_migrations` and applies only newer
     files, in filename order, each inside a transaction;
  3. treats the self-registering `INSERT` in each file as the completion marker.
- Every file must be `sqlfluff` clean (`uvx sqlfluff lint db/migrations/`,
  config in repo-root `.sqlfluff`) and must apply to a fresh PostgreSQL 16
  with zero errors — both enforced in CI (issue #11).

### Migration runner (`wavio.DbMigrator`)

`src/backend/wavio/wavio.DbMigrator` is the canonical way to apply migrations —
locally, in CI, and later on the VPS. `public.schema_migrations` doesn't exist
before V001 runs; the runner treats that as "nothing applied yet", not an error.

```bash
docker compose -f docker-compose.dev.yml up -d --wait

# Uses the ConnectionStrings__Admin env var if set, else the docker-compose.dev.yml
# default (postgres/postgres@localhost:5432/waplatform); the migrations directory is
# auto-detected by walking up from the build output to repo-root/db/migrations.
dotnet run --project src/backend/wavio/wavio.DbMigrator

# Explicit overrides (CI / non-default environments):
dotnet run --project src/backend/wavio/wavio.DbMigrator -- \
  --connection-string "Host=localhost;Port=5432;Database=waplatform;Username=postgres;Password=postgres" \
  --migrations-dir db/migrations

./db/tests/rls_smoke_test.sh
```

Applying with plain `psql` also works, since each file is a self-contained,
idempotent script:

```bash
for f in db/migrations/V0*.sql; do
  PGPASSWORD=postgres psql -h localhost -U postgres -d waplatform \
    -v ON_ERROR_STOP=1 -q -f "$f"
done
./db/tests/rls_smoke_test.sh
```

## Schema ownership map

| Schema | Created by | Owner | Contents |
|---|---|---|---|
| `tenancy` | V001 | platform | tenants (canonical, also used by core identity), tenant_settings, api_keys, external_tenant_refs |
| `waba` | V002 | platform (wa-admin) | business_accounts, phone_numbers, phone_number_events, currency_migrations, business_profiles |
| `ingest` | V003 | platform (wa-webhook) | raw_webhooks (30-day TTL), webhook_dedupe |
| `system` | V004 | platform | audit_log (append-only), **feature_flags (canonical — core identity's `FeatureFlag` entity maps here, not `kernel`)**, jobs, job_runs |
| `identity_access` | V005 | core identity | users, user_profiles, roles, permissions, role_permissions, user_permission_override, user_scope_memberships, refresh_tokens, password_resets, otp_codes, login_history, audit_logs |
| `kernel` | V006 | core identity | outbox_events, outbox_consumed_events, system_settings, file_attachments (**no feature_flags — see `system`**) |
| `app` | V001 | platform | RLS helper functions + partition maintenance (no tables) |
| `messaging` | V007 | platform (wa-gateway / wa-ingest) | outbound_messages, outbound_outbox, inbound_messages, message_statuses, media_assets, suppression_list |
| `sessions` | V008 | platform (session window manager) | conversation_windows, window_events |
| `templates` | V009 | platform (wa-admin) | templates, template_versions, template_status_events, template_category_changes, template_lint_results, template_packs |

Cross-schema dependency note (Wave 2): `billing.message_costs` (V010+) carries
the **unique `wamid`** constraint (spec §6) and is the billing source of truth;
`messaging.message_statuses` only captures the raw `pricing`/`conversation`
webhook objects. `wamid` is the correlation key across
`messaging.outbound_messages` → `messaging.message_statuses` →
`billing.message_costs`.

`identity_access` and `kernel` DDL is derived 1:1 from the EF Core
configurations in `src/backend/wavio/wavio.SharedDataModel/Persistence/`
(database-first: this DDL is what the EF model must map to). There is **one**
canonical tenants table, `tenancy.tenants` — the scaffold's `tenancy_org`
schema is never created; the `Tenant` entity is remapped to `tenancy.tenants`.

## Multi-tenancy: `app.tenant_id` GUC + RLS

Every tenant-scoped table has `ENABLE` + `FORCE ROW LEVEL SECURITY` and a
policy named `tenant_isolation`:

```sql
CREATE POLICY tenant_isolation ON <schema>.<table>
USING (tenant_id = app.current_tenant_id() OR app.is_platform_admin())
WITH CHECK (tenant_id = app.current_tenant_id() OR app.is_platform_admin());
```

- `app.current_tenant_id()` reads `app.tenant_id` (spec §5 canonical GUC) and
  falls back to `app.current_tenant_id` (the name the shared EF
  `RlsConnectionInterceptor` sets). Empty string = unset = NULL → no rows.
- `tenancy.tenants` itself scopes on `id` instead of `tenant_id`.
- **Nullable-tenant pattern** (roles, feature_flags, system_settings,
  file_attachments, both audit tables): `tenant_id IS NULL OR tenant_id =
  app.current_tenant_id() OR app.is_platform_admin()` — a NULL tenant means a
  platform-global row visible to every tenant session.

### Roles

| Role | Attributes | Purpose |
|---|---|---|
| `app_user` | `LOGIN NOBYPASSRLS`, no superuser | The only runtime role. Fully subject to RLS — never grant it `platform_admin`. |
| `platform_admin` | `NOLOGIN NOBYPASSRLS` | Membership role. Policies contain `OR app.is_platform_admin()`, so members get audited cross-tenant access. Granting membership is a deliberate DDL act; every cross-tenant mutation must also be written to `system.audit_log` (app responsibility, spec §5). |
| `postgres` | superuser | Migrations + Development seeding only (`ConnectionStrings:Admin`). Never used by request-handling code. |

The `app.bypass_rls` GUC set by the EF interceptor is **deliberately ignored**
by all policies: any client can set a GUC, so it cannot be a security
boundary. DB-level bypass requires `platform_admin` membership or the admin
connection.

### Deliberately NOT tenant-scoped (no RLS) — and why

| Table(s) | Reason |
|---|---|
| `ingest.raw_webhooks`, `ingest.webhook_dedupe` | Written before tenant resolution (Meta posts to one platform endpoint); `tenant_id` is backfilled later. |
| `system.jobs`, `system.job_runs` | Platform worker infrastructure; workers run without a tenant context and could never see their queue under RLS. |
| `kernel.outbox_events`, `kernel.outbox_consumed_events` | Outbox dispatcher/consumers are background workers without tenant context. |
| `messaging.outbound_outbox` | Same rationale: the gateway dispatcher drains all tenants' queues in one scan (token-bucket per phone number). The message rows themselves (`outbound_messages`) ARE RLS-scoped — webhook-driven writers set the tenant GUC per event. |
| `identity_access` tables without `tenant_id` (users, permissions, tokens, …) | Users are platform-global; tenancy attaches via `user_scope_memberships(scope_type='tenant', scope_id)`. Access control is app-layer deny-wins RBAC (spec §5). |
| `public.schema_migrations` | Infrastructure. |

## Table conventions

- snake_case, plural table names. EF-mandated exceptions (database-first):
  `login_history`, `user_permission_override`.
- `uuid` PKs, `DEFAULT gen_random_uuid()`.
- Audit quartet `created_at/created_by/updated_at/updated_by` on mutable
  domain tables; append-only tables (events, history, audit, otp) carry only
  `created_at/created_by`.
- `timestamptz` everywhere (EF `DateTimeOffset`); `date` for civil dates.
- Soft delete via `deleted_at` where the EF model declares it (tenants, users,
  roles, file_attachments).
- Append-only enforcement: `system.audit_log` and `identity_access.audit_logs`
  grant only `SELECT, INSERT` to `app_user`/`platform_admin` — no role can
  UPDATE/DELETE audit history.

### FK audit rules (for the CI gate, issue #11)

Every **uuid** column ending `_id` must have a FK. Exclusions the gate must
allowlist (all deliberate, all commented at the column/table definition):

- Polymorphic references (discriminator column decides the target):
  `*.resource_id`, `user_scope_memberships.scope_id`,
  `user_permission_override.scope_id`, `otp_codes.reference_id`,
  `file_attachments.owner_id`, `file_attachments.uploaded_by_id`,
  `outbox_events.aggregate_id`.
- Trace/correlation identifiers (not row references): `*.request_id`,
  `*.correlation_id`, `outbox_events.causation_id`.
- `outbox_consumed_events.event_id` — may reference another service's outbox
  arriving over RabbitMQ.
- Non-uuid `*_id` columns are external identifiers (e.g. `meta_waba_id`,
  `meta_phone_number_id`, `device_id`, `kms_key_id`, `employee_id`, `upi_id`)
  — exempt by the uuid-only rule.
- Partition children (`*_p*`) inherit parent FKs; audit parents only.
- `created_by/updated_by/granted_by/revoked_by` (`*_by`) carry no FK by
  convention: they may predate `identity_access.users` (V001–V004 run before
  V005) and must survive user hard-deletion (DPDP erasure).

V007–V009 (messaging/sessions/templates) introduce **no new exclusions**:
every uuid `*_id` column added by Wave 1 has a FK. Two are deferred to V009
(`outbound_messages.template_id` / `.template_version_id` — the templates
schema doesn't exist yet at V007), same pattern as the V004→V005
`audit_log.actor_user_id` FK. `wamid`, `wa_id`, `meta_media_id` etc. are
varchar external identifiers, exempt by the uuid-only rule.

## Retention / partitioning

- `ingest.raw_webhooks`: RANGE-partitioned by `received_at`, **weekly**
  partitions (`raw_webhooks_pYYYYMMDD`, ISO week start).
  `ingest.maintain_raw_webhooks(retain_days => 30, weeks_ahead => 4)` creates
  upcoming partitions and **drops** partitions entirely older than the
  retention window (metadata-only, no DELETE churn). Effective TTL 30–37 days.
- `system.audit_log`, `identity_access.audit_logs`: RANGE-partitioned by
  `occurred_at`, **monthly** (`*_pYYYYMM`) via
  `app.ensure_month_partitions(parent, months_ahead)`. Audit partitions are
  retained indefinitely (compliance); archive/detach is an ops decision later.
- Both have a `*_pdefault` DEFAULT partition as a safety net so writes never
  fail if maintenance lapses. Keep it empty: if rows accumulate there, creating
  the proper partition for that period will fail until the rows are moved
  (`INSERT INTO parent SELECT * FROM *_pdefault WHERE ...; DELETE ...`).
- Scheduling (OSS-only, no pg_cron): register `system.jobs` entries that call
  `SELECT ingest.maintain_raw_webhooks();` and
  `SELECT app.ensure_month_partitions('system.audit_log'::regclass, 3);` /
  `...('identity_access.audit_logs'::regclass, 3);` at least weekly. Until the
  job runner exists (Wave 1), the migrations pre-created ~4 weeks / 3 months of
  partitions ahead.
- `ingest.webhook_dedupe`: cleanup job deletes rows older than 30 days by
  `first_seen_at` (small table; plain DELETE is fine).
- `messaging.outbound_messages` 24h idempotency window: a partial unique index
  cannot reference `now()`, so uniqueness is
  `UNIQUE (tenant_id, idempotency_key) WHERE idempotency_active` plus a
  `system.jobs` task that clears `idempotency_active` on rows with
  `accepted_at < now() - interval '24 hours'`. Within the window a duplicate
  key raises a unique violation and the gateway returns the original result
  (issue #14).

## Security & PII

- API keys (`tenancy.api_keys`): only the **argon2id** encoded hash is stored
  (`key_hash`); `key_prefix` (unique) locates the row; `scope` is CHECK-limited
  to `send_only | read_only | admin`; optional `ip_allowlist inet[]`.
- Meta system-user tokens: `waba.business_accounts.system_user_token_ciphertext`
  holds app-layer envelope-encrypted ciphertext only.
- PII columns in `identity_access.user_profiles` (`pan_number`,
  `bank_account_number`, `upi_id`) store AES-256-GCM ciphertext (EF
  `PiiValueConverter`) and are `text` on purpose.
- `users.email` is `citext` (extension created in V001).

## Tests

`db/tests/rls_smoke_test.sh` — two-tenant RLS smoke test (exit ≠ 0 on any
failure; used locally and in CI): tenant A cannot see or write tenant B's rows
and vice versa (both GUC spellings), unset context sees nothing, global rows
are visible to all tenants, audit log is append-only, `app_user` is neither
superuser nor BYPASSRLS nor a `platform_admin` member. Wave 1 coverage:
`messaging.suppression_list`, `sessions.conversation_windows`,
`templates.templates` isolation plus `templates.template_packs` global rows.
