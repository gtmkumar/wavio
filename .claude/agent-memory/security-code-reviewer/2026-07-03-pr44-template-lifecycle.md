---
name: 2026-07-03-pr44-template-lifecycle
description: Security audit of PR #44 (wa-admin-svc template lifecycle, issue #16) — APPROVE; RLS+permission wiring verified end-to-end; X-Tenant-Id/ICurrentTenant gap confirmed latent-only (noted for #42)
metadata:
  type: project
---

# PR #44 security audit (2026-07-03) — wa-admin-svc template lifecycle v1

Verdict: APPROVE. 81/81 WaAdmin + 36/36 WaIngest tests pass (isolated worktree); zero warnings from the PR's own projects.

## Verified-good (don't re-flag)
- **AuthZ**: all 7 /v1/templates routes permission-gated (`Templates.cs` — group `RequireAuthorization()` + per-route `permission:templates.*`); codes seeded in `IdentitySeeder` with sane grants (tenant_admin full, staff read-only, `templates.delete` RiskLevel.High → step-up). No anonymous route. Different pattern from ingest replay (seeded vs absent-code) but deliberate: templates are tenant-facing, replay is ops-only.
- **RLS**: V009 has ENABLE+FORCE RLS + `tenant_isolation` policies (USING and WITH CHECK on `app.current_tenant_id() OR app.is_platform_admin()`) on all 6 templates tables. Query/mutation handlers intentionally have no explicit tenant filters — RLS via `RlsConnectionInterceptor` GUCs is the enforcement, consistent with core. `Database.SqlQuery` raw SQL (GetBusinessAccountMetaWabaIdAsync) is FormattableString-parameterized AND still RLS-scoped (waba.business_accounts has RLS) — cross-tenant submit fails closed.
- **ScopedCurrentTenant** (consumer): Replace() of HttpContextCurrentTenant preserves HTTP behavior byte-for-byte; consumer scopes set OverrideTenantId per message and force `BypassRls=false` when overridden — consumer can never bypass RLS; Guid.Empty tenants parked, never fabricated.
- **Graph client**: token config-only, sent only as Authorization header, never logged (log messages: status code + Meta error text only); BaseUrl config-only (not tenant-controllable → no SSRF); stub is a standalone tool project (not referenced by WebApi), Program.cs fails closed outside Dev when BaseUrl/AccessToken missing.
- **Immutability**: server-side state machine (`TemplateStatusTransitions`, DISABLED terminal, no same-status transitions); Update on non-DRAFT version creates a new DRAFT version (never mutates reviewed Components); PENDING/DISABLED edits rejected 422; name/language identity changes rejected; Delete only for DRAFT (soft delete + global query filter).
- **Consumer**: manual ack, Nack-no-requeue → DLQ (`wavio.events.dlx`/`wa-admin.template-events.dlq`), per-message DI scope, invalid transition/unknown status/unknown template/empty tenant all parked (tests cover each); poison message can't block others.
- Error paths: BusinessRuleException/ValidationException → 422, KeyNotFoundException → 404 via shared ExceptionHandler.

## X-Tenant-Id / ICurrentTenant pre-existing gap — LATENT-ONLY here (backlog #42)
`HttpContextCurrentUser.TryGetTenantId()` honors `Items["tenant_id_override"]` (X-Tenant-Id, set by TenantResolutionMiddleware **for platform_admin only**), but `HttpContextCurrentTenant`/`ScopedCurrentTenant` (RLS GUC source) ignore it. Non-admins can't set the override; for them both sources = JWT claim, and RLS WITH CHECK fails closed on any hypothetical mismatch. For admins RLS is bypassed by design and entity TenantId gets the override — coherent writes. Residual (for #42): `app.tenant_id` GUC ≠ override tenant during admin override sessions — any future SQL/policy/function trusting the GUC while bypass_rls=true would mis-scope. Fix belongs in shared ICurrentTenant, not per-service.

## Should-fix carried on this PR (non-blocking)
- Consumer catch-all converts TRANSIENT failures (DB down/timeout) into Nack→DLQ — legitimate Meta transitions permanently parked with no redrive tool. Distinguish transient (requeue/delay) from permanent (DLQ), or ship a DLQ redrive runbook.
- No upper bound on GetTemplates pageSize and no size cap on Components/ExampleValues JSON (jsonb, up to Kestrel 30MB) — authenticated-tenant resource abuse; cap pageSize (e.g. 200) and payload size.
- Malformed ExtrasJson (`FormatException`) and JsonException from user input → 500 instead of 4xx (unmapped in ExceptionHandler).
- Nit: CreateTemplate doesn't validate BusinessAccountId belongs to the tenant at create time (FK checks bypass RLS) — submit fails closed later via RLS lookup; unguessable UUID required.
