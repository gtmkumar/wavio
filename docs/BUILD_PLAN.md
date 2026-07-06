# wa-platform-core — Build Plan

**Derived from:** [`WHATSAPP_PLATFORM_CORE_PRODUCTION_SPEC.md`](./WHATSAPP_PLATFORM_CORE_PRODUCTION_SPEC.md) §10
**Date:** 2026-07-03
**Tracking:** GitHub milestones Wave 0–4, one epic issue per wave, tasks as sub-issues.

| Wave | Epic | Milestone exit |
|---|---|---|
| Wave 0 — Foundations | [#1](https://github.com/gtmkumar/wavio/issues/1) | Meta app + ES applied, decisions resolved, DB/CI/VPS baseline in place |
| Wave 1 — Send/Receive Core | [#2](https://github.com/gtmkumar/wavio/issues/2) | 8-scenario live smoke green against real test number |
| Wave 2 — Money & Safety | [#3](https://github.com/gtmkumar/wavio/issues/3) | **G1:** DocSlot on platform core in staging |
| Wave 3 — Rich Channel | [#4](https://github.com/gtmkumar/wavio/issues/4) | **G2:** Laundry Ghar live on platform core |
| Wave 4 — Intelligence | [#5](https://github.com/gtmkumar/wavio/issues/5) | **G3:** external-API go/no-go decision |

---

## Hosting & cost constraints (deviation notes vs spec)

We self-host on a **single VPS** and use **free / open-source software only** — no managed cloud services. The spec's stack is already fully OSS (.NET 10, PostgreSQL 16, RabbitMQ, YARP, Aspire), so functional architecture is unchanged; the adaptations below are operational.

### Runtime: Docker Compose on the VPS
- `docker-compose.prod.yml` runs the 5 services + PostgreSQL 16 + RabbitMQ + **Caddy** as the reverse proxy.
- Caddy provides automatic Let's Encrypt TLS — Meta webhooks require a valid public HTTPS endpoint.
- **.NET Aspire AppHost is local-dev orchestration only**; it is not used on the VPS.
- Firewall (ufw): only 80/443/SSH exposed. PostgreSQL and RabbitMQ are never publicly reachable.
- Images built by GitHub Actions, pushed to GHCR (free tier), deployed via SSH + `docker compose up -d`.

### Database
- Single PostgreSQL instance on the VPS, database **`waplatform`**, DDD schema split per spec §6.
- Dev connection string: `Host=localhost;Port=5432;Database=waplatform;Username=postgres;Password=postgres`.
- **Production must use a dedicated role and strong password** — the dev credentials above are local-only.
- Tenant isolation via RLS + `app.tenant_id` GUC on every tenant-scoped table (spec §5).
- Migrations: versioned SQL files `V001__…` onward, sqlfluff-clean, validated against a real PostgreSQL service container in CI, FK-audit gate mandatory.

### Secrets (no cloud KMS)
- Wherever the spec says "KMS": **app-level envelope encryption** with a master key kept in a `0600` env file on the VPS.
- Secrets-at-rest in the repo/deploy pipeline: **SOPS + age** (both free).
- Applies to Meta system-user tokens, webhook app secret (rotated quarterly), and Flows data-exchange private keys.

### Observability (all self-hosted, free)
- Serilog + OpenTelemetry in every service; correlation ID = wamid chain; wa_id masked, message bodies excluded from traces.
- Dev: Aspire dashboard. Prod: self-hosted **Grafana + Prometheus + Loki** added to compose when needed (Wave 2+).

### Backups / DR
- v1: nightly `pg_dump` cron with rotation + periodic restore drill.
- Spec §8 target (PITR, RPO 5 min) is a **Wave 2+ hardening task** via pgBackRest or wal-g — both free.

### CI
- GitHub Actions free tier: build/test, sqlfluff, FK-audit gate, migration validation against real Postgres, contract tests.

---

## Waves

### Wave 0 — Foundations ([#1](https://github.com/gtmkumar/wavio/issues/1))
Blockers for everything else.

- [#6](https://github.com/gtmkumar/wavio/issues/6) Meta app setup: Embedded Signup approval, test WABA + INR numbers
- [#7](https://github.com/gtmkumar/wavio/issues/7) Resolve open decisions: OD-1 (AI Providers policy — blocks Wave 4), OD-2 (number strategy), OD-3 (custom CQRS)
- [#8](https://github.com/gtmkumar/wavio/issues/8) Repo scaffold: .NET 10 solution, 5 services, Aspire AppHost, `WaPlatform.Contracts`
- [#9](https://github.com/gtmkumar/wavio/issues/9) Dev infra: docker-compose (PostgreSQL 16 + RabbitMQ), `waplatform` bootstrap
- [#10](https://github.com/gtmkumar/wavio/issues/10) DB migrations V001–V004: `tenancy`, `waba`, `ingest`, `system` + RLS
- [#11](https://github.com/gtmkumar/wavio/issues/11) CI pipeline: build/test, sqlfluff, FK-audit gate, migration validation
- [#12](https://github.com/gtmkumar/wavio/issues/12) VPS deployment baseline: prod compose, Caddy TLS, SOPS/age secrets, backup cron

### Wave 1 — Send/Receive Core ([#2](https://github.com/gtmkumar/wavio/issues/2))
- [#13](https://github.com/gtmkumar/wavio/issues/13) wa-ingest-svc: HMAC verify, <1s ack, raw persist, wamid dedupe, normalize, publish
- [#14](https://github.com/gtmkumar/wavio/issues/14) wa-gateway-svc: `POST /v1/messages`, idempotency, outbox, retries, two-layer rate limiting
- [#15](https://github.com/gtmkumar/wavio/issues/15) Session Window Manager + window-aware send rejection (ADR-005)
- [#16](https://github.com/gtmkumar/wavio/issues/16) Template lifecycle v1: CRUD, submit, status webhooks, state machine, lint stub
- [#17](https://github.com/gtmkumar/wavio/issues/17) DB migrations V005–V008: `messaging`, `sessions`, `templates`
- [#18](https://github.com/gtmkumar/wavio/issues/18) QA: 8-scenario live smoke suite (wave exit)

### Wave 2 — Money & Safety ([#3](https://github.com/gtmkumar/wavio/issues/3))
- [#19](https://github.com/gtmkumar/wavio/issues/19) Cost & Billing engine: rate cards + quarterly refresh, webhook-`pricing` cost ledger, estimator, quotas
- [#20](https://github.com/gtmkumar/wavio/issues/20) Quality Rating Guardian: quality/tier ingestion, auto-throttle, health reports
- [#21](https://github.com/gtmkumar/wavio/issues/21) Consent ledger (DPDP): opt-in evidence, STOP listener, suppression, erasure
- [#22](https://github.com/gtmkumar/wavio/issues/22) Campaign engine: tier-aware chunking
- [#23](https://github.com/gtmkumar/wavio/issues/23) DB migrations V009–V012: `billing`, `quality`, `consent`
- [#24](https://github.com/gtmkumar/wavio/issues/24) **Exit gate G1:** DocSlot migrated onto platform core in staging

### Wave 3 — Rich Channel ([#4](https://github.com/gtmkumar/wavio/issues/4))
- [#25](https://github.com/gtmkumar/wavio/issues/25) Flows engine + DocSlot booking & Laundry Ghar pickup flow packs
- [#26](https://github.com/gtmkumar/wavio/issues/26) UPI payments: `order_details`, status webhooks, reconciliation, PSP fallback
- [#27](https://github.com/gtmkumar/wavio/issues/27) Template policy lint v1: static rules + LLM check (>90% first-pass approval)
- [#28](https://github.com/gtmkumar/wavio/issues/28) Max-price bidding config (feature-flagged until Meta open beta ~Oct 2026)
- [#29](https://github.com/gtmkumar/wavio/issues/29) **Exit gate G2:** Laundry Ghar live on platform core

### Wave 4 — Intelligence ([#5](https://github.com/gtmkumar/wavio/issues/5))
- [#30](https://github.com/gtmkumar/wavio/issues/30) AI Orchestration gateway: intent routing, pgvector KBs, guardrails, eval loop (gated on OD-1)
- [#31](https://github.com/gtmkumar/wavio/issues/31) Analytics marts + campaign funnel dashboards (Grafana)
- [#32](https://github.com/gtmkumar/wavio/issues/32) NLQ / text-to-SQL over marts
- [#33](https://github.com/gtmkumar/wavio/issues/33) Warehouse export connectors (Parquet → self-hosted MinIO)
- [#34](https://github.com/gtmkumar/wavio/issues/34) **Exit gate G3:** external API decision point

---

## Hard dates to watch (from spec)

- **2026-12-31** — all WABAs must be INR-billed; Meta stops delivering from non-INR WABAs 2027-01-01. Provision INR from day one (#6); hard alerts from 2026-10.
- **Quarterly (Jan/Apr/Jul/Oct 1)** — Meta rate-card updates; refresh job in #19.
- **~Oct 2026** — max-price bidding open beta; flag flips in #28.

## Open decisions (tracked in #7 — resolution records in `docs/OPEN_DECISIONS.md`, 2026-07-06)

| ID | Question | Blocks | Outcome |
|---|---|---|---|
| OD-1 | Does the AI gateway make us an "AI Provider" under Meta's 2026 policy? | Wave 4 GA | Not an AI Provider (engineering position); build unblocked, GA needs owner/legal sign-off |
| OD-2 | Laundry Ghar: number per outlet vs shared number + routing | Wave 3 (G2) | Shared brand number + outlet routing; per-outlet escalation on volume/quality triggers |
| OD-3 | Confirm ADR-007 custom CQRS (no MediatR) | Wave 1 code style | ACCEPTED — confirmed by shipped Wave 1–2 code |
| OD-4 | Marketing Messages Lite API adoption | Campaign engine (non-blocking) | Deferred to post-G2 volume data; Graph-client seam preserved |
| OD-5 | Tenant channel pricing: bundled quota + pass-through overage (recommended) | Billing plans (non-blocking) | Adopted: bundled quota + pass-through overage |
