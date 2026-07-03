---
name: rls-background-service-guc-gotcha
description: A background service (no HttpContext) writing through the shared WavioDbContext silently fails RLS unless the app.tenant_id GUC is set explicitly and the connection is held open
metadata:
  type: project
---

Any hosted background service (BackgroundService/IHostedService — a RabbitMQ consumer, a
periodic scanner, anything not running inside an ASP.NET request) that writes to an RLS-protected
table through the shared `WavioDbContext` will get `42501: new row violates row-level security
policy` even when the write logic is otherwise correct. Found live during issue #15's Session
Window Manager verification (the RabbitMQ consumer for `wa.message.received.v1` — see
[[issue-15-session-windows]]).

**Why:** `RlsConnectionInterceptor` (wavio.SharedDataModel/Persistence/Interceptors) sets the
`app.tenant_id` Postgres GUC from `ICurrentTenant.TenantId`. Every service's registered
`ICurrentTenant` is `HttpContextCurrentTenant`, which reads a JWT claim off `HttpContext.User` —
and there is no `HttpContext` in a background service, so `TenantId` is always null there,
regardless of how correctly the background code itself resolved the real tenant.

Worse: the interceptor only fires on `ConnectionOpened`/`ConnectionOpenedAsync`, and **EF Core
opens and closes its underlying connection implicitly around each individual operation** unless
something holds it open — so even manually issuing `SELECT set_config('app.tenant_id', ...)` via
`Database.ExecuteSqlInterpolatedAsync` gets silently undone the next time EF re-opens the
connection for the next command (the interceptor re-runs, resets it to empty). The fix has two
parts, both required:
1. `await _db.Database.OpenConnectionAsync(ct)` first, to hold the connection open for the rest
   of the unit of work's lifetime (so the interceptor only fires once, before the override).
2. Then issue the explicit `set_config('app.tenant_id', <realTenantId>, false)`.

**How to apply:** Any future service adding a background writer against RLS-protected tables
needs this same two-step override — it is not specific to wa-intel-svc. Consider promoting a
shared helper (e.g. `IDbContextTenantOverride` or an extension method) to `wavio.SharedDataModel`
so the next agent doesn't have to rediscover this via a live-verification failure; issue #15's
`WaIntel.Application.Common.Interfaces.IWaIntelDbContext.SetTenantContextAsync` /
`WaIntel.Infrastructure.Persistence.WaIntelDbContext.SetTenantContextAsync` is the reference
implementation. This was NOT caught by unit tests, because the EF Core in-memory provider used by
`InMemoryWaIntelDbContext` doesn't enforce RLS at all — only a real Postgres live-verification run
surfaces it, which is why "verify live" is a hard requirement for anything touching RLS-scoped
writes, not an optional nice-to-have.
