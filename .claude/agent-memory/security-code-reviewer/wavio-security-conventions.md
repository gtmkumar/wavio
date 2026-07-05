---
name: wavio-security-conventions
description: Platform security mechanisms verified in the Wavio codebase — JWT posture, PII masking, exception handling, secrets posture — check these before flagging
metadata:
  type: project
---

# Wavio platform security conventions (verified 2026-07-03, PR #41 audit)

- **JWT**: every service host is validate-only. RS256 pinned via `ValidAlgorithms = [RsaSha256]`, issuer+audience+lifetime validated, JWKS from `Jwt:Authority` (wavio-identity). Convention: Issuer `wavio-identity`, Audience `wavio-services`. See each `*.WebApi/Program.cs`. Permission machinery exists (`PermissionHandler`, `AnyPermissionHandler`, `PermissionPolicyProvider`, `StepUpAuthorizationResultHandler` in `wavio.Utilities/Auth`) but Wave 1 endpoints often use bare `RequireAuthorization()` = any authenticated user.
- **PII masking**: `wavio.ServiceDefaults/Logging/WaPiiMask.cs` — `MaskWaId()` for call sites + `WaIdMaskingEnricher` (Serilog enricher, registered globally in `ServiceDefaults/Extensions.cs:69`) rewrites properties named WaId/wa_id/CustomerWaId/RecipientWaId/SenderWaId/To (digit-only). Does NOT cover destructured objects one level deep — flag any destructuring of customer objects into logs. wamid is treated as non-PII and used as correlation (`WamidCorrelationMiddleware`).
- **Error responses**: `wavio.Utilities/Middlewares/ExceptionsMiddleware/ExceptionHandler.cs` maps PG SQLSTATEs to clean client messages; full exception logged server-side only; client body never carries inner DB text.
- **Secrets posture**: config-only, Development gets a clearly-labelled fallback string, every other environment throws at startup (pattern origin: `wavio.SharedDataModel/DependencyInjection` PII key; repeated for `Meta:Webhook:*` in WaIngest Program.cs). Watch for services that skip the fail-closed half (e.g. RabbitMQ conn string fallback in PR #41).
- **RLS/tenancy**: tenant-scoped tables use PG RLS via `app.tenant_id` GUC; `ingest.*` tables are deliberately NOT tenant-scoped (documented in db/migrations/V003 header).
- **Bus**: exchange `wavio.events` (topic, durable, no auto-delete), routing key = event name, contract rule: consumers must be idempotent on EventId (at-least-once).
- **Spec anchor**: `docs/WHATSAPP_PLATFORM_CORE_PRODUCTION_SPEC.md` — §4.3 ingestion, §5 security/PII, §7.2 events, §8 NFRs.
