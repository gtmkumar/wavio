# Security Code Reviewer — memory index

- [Wavio security conventions](wavio-security-conventions.md) — JWT RS256 validate-only, WaPiiMask enricher, non-leaky ExceptionHandler, fail-closed secrets posture
- [2026-07-03 PR #41 audit](2026-07-03-pr41-ingest-webhooks.md) — wa-ingest-svc webhook receiver: APPROVE after fixes (c201757); all 6 findings closed, 36/36 tests; replay authz = platform_admin-only via absent catalog code
- [2026-07-03 PR #40 audit](2026-07-03-pr40-vps-deploy.md) — VPS deploy baseline: APPROVE; Should-fixes gated on first real deploy (JWT http-authority break, ufw/Docker bypass doc, ssh-action pinning, backup perms, AAD)
- [2026-07-03 PR #44 audit](2026-07-03-pr44-template-lifecycle.md) — wa-admin template lifecycle: APPROVE; RLS/permission wiring verified; X-Tenant-Id gap latent-only → #42; DLQ-transient + size-cap Should-fixes
- [2026-07-03 PR #43 audit](2026-07-03-pr43-session-windows.md) — wa-intel session windows: APPROVE; RabbitMq S2 fail-open recurs; raw wa_id in URL path = new trace/log PII; simulate double-gate + scanner GUC verified
- [2026-07-03 PR #45 audit](2026-07-03-pr45-gateway-send.md) — wa-gateway send API: APPROVE; lease-vs-HttpClient-timeout double-send + missing eager Meta:Graph boot guard = key Should-fixes; cross-tenant send fails closed via RLS at dispatch
