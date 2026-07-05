---
name: platform-admin-write-rls-gap
description: A platform_admin JWT + X-Tenant-Id header cannot WRITE to any strict-RLS table via the normal app_user connection — found live during issue #21
metadata:
  type: project
---

`ICurrentUser.RequireTenantId()` (the correct convention for any endpoint a platform_admin might
call, per [[issue-20-quality-guardian]]'s bug 2) resolves the `X-Tenant-Id` override fine at the
Application layer — but that resolved tenant id is NOT what ends up in the `app.tenant_id` Postgres
GUC. `wavio.SharedDataModel.Persistence.Interceptors.RlsConnectionInterceptor` sets the GUC from
`ICurrentTenant.TenantId`, which is the raw JWT `tenant_id` claim only (no `X-Tenant-Id`
awareness) — a platform_admin token never carries that claim at all. `app.is_platform_admin()`
(V001) doesn't help either: it's `pg_has_role(current_user, 'platform_admin', 'member')`, a
Postgres ROLE-membership check, and every service connects as `app_user`, which the schema's own
comment says must NEVER be granted the `platform_admin` DB role. Net effect: a platform_admin
calling ANY endpoint that writes to a strict-RLS (non-nullable-tenant) table via an `X-Tenant-Id`
override gets `new row violates row-level security policy` — the DB session has no real tenant
context and doesn't pass the platform-admin OR-clause either.

**Why:** confirmed live in issue #21 (Consent ledger) trying to call `POST /v1/consent/opt-in` as
platform_admin + `X-Tenant-Id`; [[issue-20-quality-guardian]] already hit the read-side version of
this (`ICurrentTenant.TenantId` vs `ICurrentUser`), but writes fail even when the endpoint code
itself is 100% correct, because the gap is in `RlsConnectionInterceptor`, not the handler.

**How to apply:** for ANY live verification (or real usage) of a platform_admin write path against
a strict-RLS table, do NOT rely on platform_admin + `X-Tenant-Id` — create a throwaway
tenant-scoped `tenant_admin`/`staff` fixture user instead (copy the seeded admin's password hash to
skip the app's hasher), same workaround both #20 and #21 used. A real fix (not yet built) would
teach `RlsConnectionInterceptor` to also read `HttpContext.Items["tenant_id_override"]` — that's a
cross-cutting `wavio.SharedDataModel`/`wavio.Utilities` change, out of scope for any single
vertical-feature issue; flag it up rather than silently re-discovering this every time.
