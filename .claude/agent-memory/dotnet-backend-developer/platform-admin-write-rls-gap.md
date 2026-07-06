---
name: platform-admin-write-rls-gap
description: RESOLVED (admin console slice, 2026-07-06) — platform_admin + X-Tenant-Id now reaches the app.tenant_id GUC via the ICurrentTenant implementations
metadata:
  type: project
---

**RESOLVED 2026-07-06** (admin-web console slice). The fix anticipated below was implemented at
the `ICurrentTenant` layer (one rung above `RlsConnectionInterceptor`, which needed no change):
all three implementations now resolve `HttpContext.Items["tenant_id_override"]` (set by
`TenantResolutionMiddleware` ONLY for `user_type=platform_admin`) ahead of the JWT `tenant_id`
claim, so the RLS GUC scopes reads AND writes to the acted-on tenant:

- `wavio.Utilities/Services/HttpContextCurrentTenant.cs` (core, WaBilling, WaIntel, WaIngest)
- `WaAdmin.Infrastructure/Messaging/ScopedCurrentTenant.cs` (background-consumer OverrideTenantId still wins)
- `WaGateway.Infrastructure/Persistence/ScopedCurrentTenant.cs` (outbox-dispatcher OverrideTenantId still wins)

Verified live: platform_admin + `X-Tenant-Id` listing `waba.phone_numbers` (FORCE RLS) returns the
tenant's rows; without the header it correctly returns nothing. Full campaign create/launch as
platform_admin + override also verified end-to-end. All 570 tests green.

Original finding (kept for context): `RlsConnectionInterceptor` sets `app.tenant_id` from
`ICurrentTenant.TenantId`; that used to be the raw JWT claim only, and a platform_admin token
carries no tenant claim, so every strict-RLS write failed with `new row violates row-level
security policy` and every read returned empty. `app.is_platform_admin()` is a Postgres
ROLE-membership check (`pg_has_role(current_user, 'platform_admin', 'member')`) and `app_user`
must never hold that role, so the OR-clause never rescued app connections. The old workaround
(throwaway tenant-scoped fixture user) is no longer needed.
