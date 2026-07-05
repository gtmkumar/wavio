---
name: 2026-07-03-pr43-session-windows
description: Security audit of PR #43 (wa-intel-svc Session Window Manager, issue #15) — APPROVE with Should-fixes; RabbitMq S2 defect recurs; raw wa_id in URL path is a new trace/log PII exposure
metadata:
  type: project
---

# PR #43 security audit (2026-07-03) — wa-intel-svc Session Window Manager

Verdict: APPROVE (no Blocking). 24/24 WaIntel tests pass (isolated worktree). Two Should-fixes, both non-blocking for Wave 1.

## Should-fix
- **S1 — RabbitMq fail-open recurs (the PR#41-S2 / PR#40-S2 defect)**: `WaIntel.Infrastructure/Messaging/RabbitMqConnectionManager.cs:24-27` falls back to `amqp://guest:guest@localhost:5672` in ALL environments, and `WaIntel.WebApi/Program.cs` has NO eager RabbitMq guard (WaIngest+WaAdmin both got the two-layer fail-closed fix; WaIntel was branched off #13 before that landed). Add IHostEnvironment ctor guard + Program.cs boot check.
- **S2 — raw wa_id in URL path is a NEW PII surface**: `GET /api/v1/windows/{waId}` (`WaIntel.WebApi/Endpoints/Windows.cs:28`) puts the customer wa_id in the path. The `WaIdMaskingEnricher` only masks structured log properties named WaId/wa_id/etc — it does NOT touch `RequestPath`/OTel `url.path`, so a raw wa_id lands unmasked in access logs / traces. Spec §5 treats wa_id as PII-to-mask. First endpoint in the platform with wa_id in the path (webhook payloads carry it in the body). Hash/opaque-id the path param, or document+suppress path capture. (Conditional on request-logging/OTel path capture being enabled.)

## Nit
- WindowClosingV1 (MessagingEvents.cs:82) has no `is_simulated` flag — simulated windows emit real closing events indistinguishable downstream. Bounded: simulate is non-prod-only, but a staging consumer can't tell.
- Scanner sets `app.tenant_id` per tenant but not `app.bypass_rls` explicitly; relies on Npgsql reset-on-close + interceptor-always-sets to avoid stale GUC across pooled leases. Safe in practice; explicit reset would be defense-in-depth. (`WindowClosingScannerService.cs:100-107`)

## Verified-good (don't re-flag)
- **Simulate double-gate both fail closed**: route only mapped when `!IsProduction()` (`Windows.cs:35-42`) AND handler throws in `IsProduction()` (`SimulateWindowHandler.cs:31`). Non-prod path still `RequireAuthorization()` + derives tenant from JWT, so a QA user can only simulate for their OWN tenant (RLS WITH CHECK enforces). is_simulated=true persisted.
- **GET RLS chain**: new `WaIntelDbContext` adapts shared `WavioDbContext` (RLS interceptor attached); V008 has `FORCE ROW LEVEL SECURITY` + `WITH CHECK` on both sessions tables; endpoint reads tenant from `ICurrentTenant` (JWT), null→401. platform_admin (null TenantId) returns 401 — tenant-scoped only, acceptable.
- **Background tenant context**: Admin/superuser connection used ONLY to `SELECT id FROM tenancy.tenants` (never touches window data); per-tenant scan/claim runs on app_user connection with `set_config('app.tenant_id',...)` then RLS-scoped SELECT+UPDATE. GUC-per-tenant iteration correct; cross-iteration leak mitigated by fresh-connection-per-tenant + set-before-use + Npgsql reset-on-close + interceptor. Audit row per scan cycle written (`IAuditWriter`, tenant_id NULL via nullable-tenant RLS) and meaningful (tenantsScanned/notificationsEmitted).
- **SetTenantContextAsync**: OpenConnectionAsync pins the connection so RlsConnectionInterceptor fires once, session-level set_config survives — documented live-found gotcha, sound.
- **Parked events**: acked-no-requeue, documented; recovery is ingest raw_webhooks replay (documented). Acceptable Wave 1 floor — no parked-events table needed yet. Never writes Guid.Empty-tenant rows.
- **LISTEN/NOTIFY**: `NotifyAsync` parameterizes channel+payload via ExecuteSqlInterpolated (pg_notify(text,text)); channel never caller-supplied. Poisoning/storm requires direct SQL access (app_user), NOT tenant-reachable; worst case is a cache eviction → RLS-scoped DB reread. Not cross-tenant exploitable.
- **Contract**: `Referral` is an additive nullable `init` property on the `MessageReceivedV1` record (property-init, not positional) — no reorder/rename, safe for System.Text.Json consumers. Note: wa-ingest normalizer doesn't populate it yet (documented gap).
- New Npgsql 10.0.3 dep; RabbitMQ.Client 7.1.2 (no advisories, per prior audits).

See [[wavio-security-conventions]], [[2026-07-03-pr41-ingest-webhooks]] (S2 origin), [[2026-07-03-pr44-template-lifecycle]] (same RLS/consumer patterns).
