---
name: handoff-issue-10
description: Handoff notes for service devs (Wave 1 V007-V009) and the dotnet developer (issue #10)
metadata:
  type: project
---

# Handoff — Wave 1 schemas, issue #17 (2026-07-03)

For the service developers building #13/#14/#15/#16 against V007–V009
(branch `feature/17-wave1-migrations`, stacked on `feature/10-db-migrations`):

- **Gateway (#14)**: on duplicate Idempotency-Key expect a unique violation on
  `outbound_messages_tenant_id_idempotency_key_key` → return the original
  result. A system.jobs task must clear `idempotency_active` on rows with
  `accepted_at < now() - interval '24 hours'` (job runner = Wave 1 work).
  The outbox dispatcher must connect WITHOUT tenant context (outbound_outbox
  has no RLS); lease rows via locked_by/locked_at + status='dispatching'.
  Permanent Graph errors (131026/131047/131049) → status='dead'.
- **Ingest (#13)**: status/message consumers must set the tenant GUC per event
  before writing message_statuses / inbound_messages (RLS-scoped). pricing and
  conversation webhook objects go into message_statuses jsonb as raw capture;
  billing.message_costs (Wave 2) is the billing source of truth.
- **Session Window Manager (#15)**: upsert onto
  conversation_windows(tenant_id, phone_number_id, user_wa_id). The
  wa.window.closing scanner is cross-tenant — it needs a DB role granted
  platform_admin (audited) or per-tenant GUC iteration; plain app_user sees
  zero rows by design.
- **Templates (#16)**: status transitions are app-enforced (CHECK only
  constrains values) — log every transition to template_status_events.
  current_version_id maintained by the app. Category changes must set
  tenant_alerted_at + billing_recalibrated_at once handled.
- **No entries were added to db/tests/fk_audit_allowlist.txt** (file lives on
  feature/11-ci-pipeline): all Wave 1 uuid *_id columns have FKs, so the CI
  gate needs no changes for V007–V009.

# Handoff to dotnet developer — issue #10 (2026-07-03)

Branch: `feature/10-db-migrations` (continue here; DB side is done and
verified). Canonical schema = `db/migrations/V001..V006`. Do not edit applied
migrations — fix forward.

## Required C# remaps (the only expected code changes)
1. **Tenant entity**: `TenantConfiguration.cs` → change
   `b.ToTable("tenants", "tenancy_org")` to `("tenants", "tenancy")`. Column
   shape is already exact — no other change; no column conflicts found.
2. **FeatureFlag entity**: `FeatureFlagConfiguration.cs` → change
   `b.ToTable("feature_flags", "kernel")` to `("feature_flags", "system")`.
   `kernel.feature_flags` does not exist; `system.feature_flags` matches the
   EF column shape exactly.

## Migration runner expectations (db/README.md "Migration convention")
- Run on the **Admin** connection (postgres), not app_user.
- Apply `db/migrations/V*.sql` in filename order, skipping versions already in
  `public.schema_migrations`; each file self-registers its version row.
- Forward-only; each file in one transaction.

## Things that will bite you if ignored
- The `RlsConnectionInterceptor` GUC name mismatch is already handled in SQL
  (policies read both `app.tenant_id` and `app.current_tenant_id`). BUT
  `ICurrentTenant.BypassRls`/`app.bypass_rls` has **no effect at the DB** —
  cross-tenant work needs the Admin connection (seeding) or a role granted
  `platform_admin`. Never grant platform_admin to app_user.
- Seeding must use `SeedingSupport.CreatePrivilegedContext` (Admin conn) —
  RLS WITH CHECK will reject cross-tenant bootstrap inserts as app_user.
- `system.audit_log` and `identity_access.audit_logs` are append-only by
  grants: EF must never UPDATE/DELETE those entities (current model doesn't).
- Both audit tables are range-partitioned; composite PK `(id, occurred_at)`
  already matches AuditLogConfiguration.
- users email/phone unique indexes are partial (`WHERE deleted_at IS NULL`) —
  duplicate-email exceptions only fire for live rows; matches soft-delete
  query filters.
- Reads with no tenant GUC set return **zero rows** on tenant-scoped tables
  (empty string = unset). Tenant resolution must happen before tenant-scoped
  queries (login flows on users/roles are fine: users has no RLS, roles with
  tenant_id NULL are visible).
- Register recurring `system.jobs` for partition/TTL maintenance when the job
  runner lands (Wave 1): `ingest.maintain_raw_webhooks()`,
  `app.ensure_month_partitions('system.audit_log'::regclass, 3)`,
  `app.ensure_month_partitions('identity_access.audit_logs'::regclass, 3)`,
  webhook_dedupe DELETE older than 30 days. ~4 weeks / 3 months are
  pre-created by the migrations.

## For the CI dev (issue #11)
- FK-audit gate rule: every **uuid** `*_id` column needs a FK; allowlist and
  rationale are in db/README.md ("FK audit rules"); exclude partition children.
- Gates to wire: sqlfluff lint (`uvx sqlfluff lint db/migrations/`), apply
  V001..V006 to a fresh PG16 service container, run
  `db/tests/rls_smoke_test.sh` (V001 creates app_user idempotently, so a bare
  PG16 container works).
