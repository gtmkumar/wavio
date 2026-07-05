---
name: 2026-07-03-pr45-gateway-send
description: Security audit of PR #45 (wa-gateway-svc outbound send API, issue #14) — APPROVE with Should-fixes; lease/HttpClient-timeout double-send window and missing eager Meta:Graph boot guard are the load-bearing findings
metadata:
  type: project
---

# PR #45 security audit (2026-07-03) — wa-gateway-svc outbound send API

Verdict: APPROVE (no Blocking). 54/54 WaGateway tests pass (isolated worktree).

## Should-fix
- **S1 — lease vs HttpClient-timeout double-send window (single-instance reachable)**: stale-lease reclaim is 30s (`OutboxDispatcherService.cs:66`), but the Graph HttpClient has NO explicit Timeout (default 100s, Polly removed). A Graph call slower than 30s → lease reclaimed while still in flight → second send of the same message; completion writes (`:213-223`) are unfenced (no `WHERE locked_by = _instanceId` compare-and-set). The documented "crash-window duplicate" honesty doesn't cover this slow-but-alive path. Fix: Graph client Timeout < staleLockTimeout AND fence completion/backoff updates with a conditional ExecuteUpdate on locked_by.
- **S2 — no eager fail-closed boot guard for Meta:Graph (and RabbitMq half-missing)**: WaGateway Program.cs has neither the Meta:Graph:BaseUrl/AccessToken guard (WaAdmin has it) nor the eager RabbitMq check (ctor guard exists but fires lazily on first publish). Prod misconfig ⇒ host boots, dispatcher throws per entry (relative-URI without BaseAddress), entries loop dispatching/reclaim forever with unbounded Attempts, and failure events are lost when the publisher ctor throws AFTER MarkDeadAsync. Add the standard eager checks.
- **S3 — phone ownership not validated at accept time**: `SendMessageHandler` never checks `command.PhoneNumberId` exists/belongs to the caller's tenant. Fails CLOSED at dispatch (RLS-scoped WabaPhoneNumbers lookup under the entry's tenant → null → dead-letter UNRESOLVED_PHONE_NUMBER + failure event), so no cross-tenant send is possible — but a cross-tenant/bogus phone id is "accepted" then asynchronously dead-lettered. One RLS-scoped `AnyAsync` at accept time gives a clean 404/422 and stops junk outbox entries.
- **S4 — no payload size cap** (same as PR#44 S2): Payload JsonElement up to Kestrel 30MB into jsonb; add request size limit.

## Verified-good (don't re-flag)
- AuthZ: single route, `permission:messages.send` + ValidationFilter (`Messages.cs:24-26`); code seeded with tenant_admin/staff grants. Tenant from ICurrentTenant JWT; null → 401.
- Idempotency: per-tenant lookup + V007 partial unique index `(tenant_id, idempotency_key) WHERE idempotency_active`; 23505 fallback returns ORIGINAL row (incl. rejections); cross-tenant key collision impossible (index tenant-scoped + RLS). The any-23505-is-idempotency assumption is sound (wamid index is `WHERE wamid IS NOT NULL`, null at insert).
- JWT forwarding to WaIntel: caller's own bearer, BaseUrl config-only (not tenant-controllable), only called from HTTP request scope. WaIntel down/non-200/expired-token → null → free-form REJECTED (fail closed per ADR-005), utility template billable=true (worst case). Deliberate + documented. Acceptable Wave 1 floor pending service credentials.
- Dispatcher tenant correctness: ScopedCurrentTenant.OverrideTenantId per entry scope; outbound_messages/waba.phone_numbers FORCE RLS (V007/V002); outbox deliberately non-RLS (documented V007 header); BypassRls forced false when overridden.
- No SSRF: media Link is passed through to Meta in the payload; gateway never fetches it. Graph BaseUrl config-only.
- Graph client: token only in Authorization header, never logged; Aspire Polly handler removed via `RemoveAllResilienceHandlers()` on THIS client only — no other handler multiplies retries; permanent-code classification: attacker/Meta-body-controlled `error.code` can only force NON-retry (fail-fast), never infinite retry (transient requires HTTP 429/5xx which the body can't control) — not spoofable into retry loops.
- Rate limiters: keyed on internal phone Guid only after an RLS-validated phone lookup → bounded cardinality; tier gate dict bounded at tierLimit entries/phone with 24h opportunistic pruning; <=0 = unlimited skips storage.
- Contract MessageSendFailedV1: additive new event; ToWaId marked "PII — mask in logs" consistent with MessageReceivedV1/WindowClosingV1; no message body content.
- RabbitMq manager: ctor fail-closed guard present (the #43-S1 lesson applied); only the eager Program.cs half missing (→S2).

## Nits
- Backoff `Math.Pow(2, attempt)` min ~2s, cap 300 but max 5 attempts ⇒ max ~40s — fine; ComputeBackoff cap comment says 5 min (unreachable with 5 attempts).
- MessageSendFailedV1 doc mentions "SUPPRESSED" but no suppression list is implemented yet (Wave 1 gap, cosmetic).
- Typed Graph client + singleton rate limiters injected into singleton hosted service pins one HttpMessageHandler (DNS rotation) — operational nit.

See [[2026-07-03-pr43-session-windows]] (S1 lesson applied here), [[2026-07-03-pr44-template-lifecycle]] (same ScopedCurrentTenant/size-cap patterns).
