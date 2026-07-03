# WHATSAPP PLATFORM CORE — PRODUCTION SPECIFICATION

**Codename:** `wa-platform-core`
**Version:** 1.0 (Draft for review)
**Date:** 2026-07-03
**Owner:** Goutam
**Status:** SPEC — pre-build
**Consumers:** DocSlot (healthcare), Laundry Ghar (laundry franchise SaaS), BeautyConnect (beauty marketplace), future verticals

---

## 0. DOCUMENT PURPOSE

This document replaces the earlier "Twilio-for-WhatsApp" research note. That note was built on an obsolete billing model (per-conversation pricing, retired 2025-07-01) and a horizontal CPaaS strategy that has negative unit economics in the Indian market. This spec redefines the product as a **shared, multi-tenant WhatsApp Platform Core** — internal PaaS first, external API product later (Gate G3, Section 14).

Strategy in one line: **capture workflow value in verticals; commoditize the channel internally; only sell the channel externally once it powers ≥3 production verticals.**

---

## 1. EXECUTIVE SUMMARY

### 1.1 What we are building

A single production-grade WhatsApp Business Platform integration layer — WABA onboarding, message gateway, template lifecycle, session-window management, quality-rating protection, per-message cost engine, Flows, UPI payments, consent ledger, AI orchestration, and analytics event pipeline — deployed once, consumed by every vertical product via internal APIs and events.

### 1.2 What we are NOT building (v1)

- A public horizontal WhatsApp API reseller competing with AiSensy / Interakt / Wati / Gupshup / Twilio on per-message markup.
- SMS/email/voice omnichannel. WhatsApp-only by design.
- A Meta BSP application. We integrate **directly with Meta Cloud API** (no BSP middleman). The on-premise API is deprecated; Cloud API is the only path and it is free to access — Meta bills message delivery only.

### 1.3 Why vertical-first

| Model | Revenue unit | India reality (2026) |
|---|---|---|
| Horizontal BSP wrapper | ₹0.005–₹0.05 markup per message | Red ocean: AiSensy, Interakt (Jio), Wati, DoubleTick at ₹999–₹2,500/mo flat; Meta Cloud API free direct; margin structurally collapsing |
| Vertical SaaS w/ WhatsApp embedded | ₹1,500–₹15,000/mo per tenant subscription + payments take-rate | DocSlot / Laundry Ghar / BeautyConnect already scoped; workflow lock-in; channel cost is COGS, not the product |

The platform core is a cost-amortization and speed asset: build once, every new vertical gets WhatsApp in days instead of months.

---

## 2. MARKET & PRICING FOUNDATION (2026 — CORRECTED)

All billing, metering, and margin logic in this platform derives from Meta's **per-message pricing (PMP)** model. Getting this wrong invalidates the billing schema. Facts as of 2026-07:

### 2.1 Per-message pricing model

- Effective **2025-07-01**, Meta bills per **delivered template message**, not per 24-hour conversation.
- Four categories: **marketing, utility, authentication, service**. Category is fixed at template approval time and determines the rate.
- Rate is determined by **recipient country code**, not sender location.
- Billing trigger: **delivery**, not send. Failed sends are not billed.
- Webhook `pricing` object on status messages: `{ "billable": true, "pricing_model": "PMP", "type": "regular", "category": "<category>" }` — this is the source of truth for our cost ledger, not our own send log.
- `pricing_analytics` field available for per-message pricing breakdowns and tier info.

### 2.2 Free traffic (design flows to maximize this)

| Window | Trigger | What's free |
|---|---|---|
| 24-hour customer service window | User sends any message; resets per user message | All free-form (non-template) messages + **utility templates** sent inside the window. Authentication templates are NOT free in-window. |
| 72-hour CTWA window | User messages via Click-to-WhatsApp ad or FB Page CTA | All business messages for 72 hours |
| Service messages | Always | Free and unlimited since 2024-11-01 |

**Design mandate:** every vertical flow must be inbound-first where possible (QR at store/clinic counter, CTWA ads, web widget). Order updates, appointment reminders, pickup notifications = **utility** templates, ideally fired inside an open window. Marketing templates are the expensive exception, never the default.

### 2.3 India-specific obligations

- India moved to **INR local-currency billing (2026-01)**; marketing rate rose ~10% in that adjustment; authentication-international rate raised again 2026-04-01.
- **Hard deadline:** all WABAs in the business portfolio must migrate to INR by **2026-12-31**. From **2027-01-01 Meta will not deliver messages from non-INR WABAs** of eligible customers. WABA Currency Migration APIs available since 2026-06-01. → Platform onboarding must provision INR WABAs from day one; migration tooling required for any acquired/legacy WABA.
- Meta pricing updates only on quarter boundaries (Jan 1 / Apr 1 / Jul 1 / Oct 1) — rate-card refresh job scheduled accordingly.

### 2.4 Upcoming pricing mechanics to support

- **Max-price bidding** for marketing messages (limited beta mid-2026, open beta ~Oct 2026): business sets a max price per marketing delivery; Meta charges that or lower based on expected-ROI auction. Billing engine must support bid caps and variable realized cost per message.
- **"AI Providers" pricing policy** (effective 2026-02-16, updated 2026-05-12): applies to platforms leveraging WhatsApp with AI. Because our AI orchestration layer routes tenant conversations through LLMs, **legal/compliance review of this policy is a Wave-0 blocker** before AI features ship. (Open Decision OD-1.)

### 2.5 Competitive posture (India)

Direct Cloud API integration means our channel COGS = Meta base rate, zero BSP markup. Competitors reselling with markup cannot underprice us on channel cost. We do not publish per-message pricing to tenants as a product; channel cost is passed through (or bundled into quota) inside vertical subscriptions.

---

## 3. ARCHITECTURE OVERVIEW

### 3.1 Topology

```
                        ┌────────────────────────────────────────────┐
                        │      META WHATSAPP CLOUD API (Graph)       │
                        └────────▲──────────────────────┬────────────┘
                                 │ send / manage         │ webhooks
┌────────────────────────────────┴──────────────────────▼────────────┐
│                        WA-PLATFORM-CORE                             │
│                                                                     │
│  ┌──────────────┐ ┌───────────────┐ ┌────────────────────────────┐ │
│  │ WABA & Tenant │ │ Template      │ │ Webhook Ingestion Service  │ │
│  │ Onboarding    │ │ Lifecycle Svc │ │ (verify, dedupe, fan-out)  │ │
│  └──────────────┘ └───────────────┘ └────────────┬───────────────┘ │
│  ┌──────────────┐ ┌───────────────┐ ┌────────────▼───────────────┐ │
│  │ Message       │ │ Session Window│ │ Event Bus (RabbitMQ)       │ │
│  │ Gateway       │ │ Manager       │ │ integration events, outbox │ │
│  └──────┬───────┘ └───────────────┘ └────────────┬───────────────┘ │
│  ┌──────▼───────┐ ┌───────────────┐ ┌────────────▼───────────────┐ │
│  │ Quality      │ │ Cost & Billing│ │ Analytics Event Pipeline   │ │
│  │ Guardian     │ │ Engine (PMP)  │ │ (append-only event store)  │ │
│  └──────────────┘ └───────────────┘ └────────────────────────────┘ │
│  ┌──────────────┐ ┌───────────────┐ ┌────────────────────────────┐ │
│  │ Flows Engine │ │ Payments (UPI)│ │ AI Orchestration Gateway   │ │
│  └──────────────┘ └───────────────┘ └────────────────────────────┘ │
│  ┌──────────────────────────────────────────────────────────────┐ │
│  │ Consent & Identity Ledger (DPDP)                              │ │
│  └──────────────────────────────────────────────────────────────┘ │
└──────────▲──────────────────▲──────────────────▲──────────────────┘
           │ internal REST/gRPC + events          │
   ┌───────┴──────┐   ┌───────┴──────┐   ┌───────┴──────┐
   │   DocSlot    │   │ Laundry Ghar │   │ BeautyConnect│
   └──────────────┘   └──────────────┘   └──────────────┘
```

### 3.2 Technology stack

| Layer | Choice | Notes |
|---|---|---|
| Runtime | .NET 10 / ASP.NET Core | Clean Architecture; custom CQRS dispatcher (no MediatR) per `dotnet10-microservices-architect` conventions |
| Data | PostgreSQL 16+ (target 18) | Dedicated `waplatform` database, DDD schema split (Section 6); RLS for tenant isolation |
| ORM | EF Core 10, database-first | Migrations as versioned SQL files, validated against real PostgreSQL before merge |
| Messaging | RabbitMQ | Integration events, transactional outbox, retry with DLQ |
| Gateway | YARP | Internal API gateway; JWT validation, per-tenant rate limiting |
| Orchestration | .NET Aspire AppHost | Local dev + service discovery |
| Observability | OpenTelemetry + Serilog | Correlation ID = `wamid` chain end-to-end |
| Auth | JWT + refresh tokens | Service-to-service via client credentials; tenant API keys hashed at rest |

### 3.3 Service decomposition (5 services + shared library)

1. **wa-gateway-svc** — outbound send, template send, media, interactive, Flows launch; rate limiting; idempotency.
2. **wa-ingest-svc** — webhook receiver (signature verify `X-Hub-Signature-256`), dedupe on `wamid`, normalize, publish to bus.
3. **wa-admin-svc** — WABA/phone onboarding (Embedded Signup), template lifecycle, business profile, rate-card sync.
4. **wa-billing-svc** — PMP cost ledger, tenant metering, quotas, invoicing feed, max-price bid config.
5. **wa-intel-svc** — Quality Guardian, session window state, analytics event store, AI orchestration gateway.

Shared `WaPlatform.Contracts` package: integration event schemas, DTOs, template DSL.

---

## 4. CORE MODULES — FUNCTIONAL SPECIFICATION

### 4.1 WABA & Tenant Onboarding

- **Embedded Signup (ES) flow** as the only onboarding path: tenant clicks "Connect WhatsApp" inside the vertical product → Meta-hosted popup → we receive exchangeable token → create/claim WABA, register phone number, set 2-step PIN, subscribe app to WABA webhooks. No manual Business Manager gymnastics for tenants.
- WABA provisioned **INR-billed** by default (India tenants). Currency stored per WABA; migration workflow (Currency Migration APIs) for imported WABAs, with hard-stop alerting ahead of 2026-12-31.
- Business profile management: display name, category, description, address, website (2 max), profile photo.
- Official Business Account ("green tick") request workflow with eligibility pre-check.
- Phone number states tracked: `PENDING → CONNECTED → FLAGGED → RESTRICTED → BANNED`, with webhook-driven transitions and tenant notification.
- Multi-number per tenant supported (franchise model: Laundry Ghar outlets may each get a number under one WABA, or share a number with outlet routing — per-vertical decision).

### 4.2 Message Gateway (outbound)

- Single internal API: `POST /v1/messages` with typed payloads — `text`, `template`, `media`, `interactive.buttons`, `interactive.list`, `interactive.cta_url`, `interactive.flow`, `location`, `contacts`, `reaction`, `order_details` (payments).
- **Idempotency:** required `Idempotency-Key` header; 24h dedupe window in `messaging.outbound_messages`.
- **Window-aware send policy (hard rule):** gateway consults Session Window Manager before dispatch:
  - Open 24h CS window + free-form or utility → send as-is (free).
  - No open window + free-form → **reject with `WINDOW_CLOSED`**; caller must supply a template. No silent auto-conversion — verticals must handle this state explicitly.
  - Marketing template → always allowed but flagged `billable_estimate` in response.
- **Rate limiting, two layers:**
  - Meta throughput: default 80 MPS per number, auto-upgradable to 1,000 MPS — token-bucket per phone number, calibrated from `phone_number` throughput field.
  - Messaging limits (marketing-initiated unique users / 24h): tier tracked per number (`250 → 1K → 10K → 100K → unlimited`), enforced pre-dispatch; campaign engine chunks broadcasts to fit tier headroom.
- **Retry policy:** transient Graph errors (429, 5xx) → exponential backoff, max 5 attempts, jitter; permanent errors (131026 not-on-WhatsApp, 131047 re-engagement required, 131049 per-user marketing limit) → fail fast, emit typed failure event.
- **Outbox pattern:** every send is written to outbox in the same transaction as domain state; dispatcher drains outbox → Graph API → status reconciliation via webhooks.

### 4.3 Webhook Ingestion

- HMAC-SHA256 signature verification; reject unsigned/invalid.
- **Return 200 in <1s always**; all processing async. Payload persisted raw (`ingest.raw_webhooks`, 30-day retention) before parse — replay/debug capability.
- Dedupe on `wamid` + event type (Meta retries webhooks).
- Normalized events published to RabbitMQ: `wa.message.received`, `wa.message.status` (sent/delivered/read/failed), `wa.template.status_changed`, `wa.template.category_changed`, `wa.quality.changed`, `wa.tier.changed`, `wa.flow.response`, `wa.payment.status`, `wa.account.alert`.
- **Pricing capture:** `pricing` object from status webhooks written directly to `billing.message_costs` — Meta's webhook is the billing source of truth; our estimates are advisory only.

### 4.4 Template Lifecycle Manager

- CRUD + submit-to-Meta via Graph; full state machine: `DRAFT → PENDING → APPROVED / REJECTED → PAUSED → DISABLED`, plus **category reclassification events** (Meta can recategorize utility→marketing; this changes cost — tenant alert + billing recalibration mandatory).
- **Policy pre-lint (differentiator):** static ruleset + LLM check before submission — promotional language in utility templates, missing opt-out in marketing, variable-density heuristics, formatting violations. Target: >90% first-pass approval rate.
- Template pausing handling: Meta auto-pauses low-quality templates (3h → 6h → disabled). Guardian (4.6) reacts: freeze campaigns using paused template, notify tenant, suggest variant.
- Versioning: templates immutable post-approval; edits create new versions; campaigns pin versions.
- Library of pre-approved vertical template packs (appointment reminder, pickup scheduled, order ready, payment link, OTP) shipped per vertical — reviewed against category rules so utility stays utility.

### 4.5 Session Window Manager

- Tracks per (phone_number_id, user_wa_id): CS window `expires_at` (last inbound + 24h), CTWA window `expires_at` (referral entry + 72h), and origin (`organic | ctwa | fb_cta`).
- Exposed as fast lookup (Postgres + in-memory cache, LISTEN/NOTIFY invalidation — same pattern as DocSlot RBAC resolver).
- Emits `wa.window.closing` event 2h before expiry → verticals can trigger "wrap-up" utility messages while still free.
- Simulation endpoint for QA: fabricate window states in non-prod.

### 4.6 Quality Rating Guardian (differentiator module)

Nobody in the Indian market does this well. Purpose: protect tenant numbers from throttling/bans.

- Ingests `phone_number_quality_update` (GREEN/YELLOW/RED) and messaging-tier changes.
- **Auto-throttle policy:** YELLOW → marketing sends cut to 50% velocity + tenant alert; RED → marketing frozen, utility/service only, incident opened.
- Block-rate telemetry: correlate `failed` statuses + quality dips against templates/campaigns; auto-flag offending template.
- Tier-growth advisor: to move 250→1K→10K, sustain quality + volume; Guardian computes safe daily send plan.
- Weekly per-number health report to tenant (delivery %, read %, block proxy, quality trend, tier headroom).

### 4.7 Cost & Billing Engine (PMP-native)

- **Rate-card service:** Meta rate cards (category × market × volume tier, INR for India) stored versioned in `billing.rate_cards`; refresh job aligned to Meta's quarterly update calendar (Jan/Apr/Jul/Oct 1); future-dated rates loadable in advance (Meta gives ≥1 quarter notice).
- **Cost ledger:** one row per billed delivery, sourced from webhook `pricing` object; reconciles against Meta invoice exports monthly; discrepancy report.
- **Estimator:** pre-send `billable_estimate` (category, destination country, window state, current tier) so campaign UIs show projected spend before launch.
- **Tenant metering & quotas:** per-tenant monthly message quotas by category (bundled in vertical subscription plans); soft limit → alert, hard limit → marketing block (utility/service never blocked).
- **Max-price bidding support (Wave 3):** per-campaign `max_price` config; realized-cost variance analytics once open beta lands (~2026-10).
- Volume-tier awareness: utility/auth tier discounts tracked; marketing has no volume discounts — model must not assume any.

### 4.8 WhatsApp Flows Engine

- Flow JSON authoring + publish via Graph; version pinning; endpoint-backed dynamic Flows (data exchange encrypted per Meta spec, key management in platform).
- Vertical flow packs: DocSlot appointment booking + intake form; Laundry Ghar pickup scheduling + garment count; BeautyConnect stylist booking with slot picker.
- Flow response webhooks normalized to `wa.flow.response` with typed payload → vertical consumes as domain command.
- Flows replace multi-message chatbot back-and-forth → fewer messages, better completion, lower cost.

### 4.9 Payments (India — UPI on WhatsApp)

- `order_details` message type with UPI payment configuration; payment status webhooks → `wa.payment.status`.
- Gateway abstraction: WhatsApp-native UPI first; Razorpay/PayU payment-link fallback for edge cases.
- Reconciliation ledger in `billing.payment_transactions`; settlement export per tenant.
- Compliance: RBI payment-flow requirements reviewed by security agent; no card data stored (out of scope — UPI only, v1).

### 4.10 Consent & Identity Ledger (DPDP Act 2023)

- Append-only `consent.opt_in_events`: wa_id, tenant, purpose (`transactional | marketing | service`), channel of capture (web form, QR, in-chat, in-person), evidence blob (form payload hash, message wamid, uploaded proof ref), timestamp, actor.
- Opt-out: STOP-keyword listener (multi-language: EN/HI + per-vertical vocab) → immediate marketing suppression per (tenant, wa_id); suppression list enforced in gateway pre-dispatch (deny-wins).
- DPDP data-principal rights: export + erasure workflows per wa_id per tenant; configurable retention (default: message content 12 months, metadata/cost ledger 8 years for tax).
- Behalf-booking consent pattern (already proven in DocSlot WhatsApp identity flows) generalized into platform: consenting party ≠ service recipient.

### 4.11 AI Orchestration Gateway

- LLM routing for inbound conversations: intent classification → deterministic flow / RAG answer / human handoff.
- Per-tenant knowledge bases (pgvector, lazy init — lesson from the NLQ review); tool-calling into vertical APIs (slot lookup, order status).
- **Evaluation loop:** escalation-to-human rate, unanswered-intent rate, thumbs feedback via quick-reply; prompt versioning with rollback.
- Guardrails: AI never sends marketing templates autonomously; AI replies only inside open service windows (free traffic by construction).
- **Blocker:** Meta "AI Providers" pricing/policy compliance review (OD-1) gates GA of this module.

### 4.12 Analytics Event Pipeline

- Append-only `analytics.events` (partitioned monthly): every send, status, inbound, window transition, flow step, payment, quality change — with tenant, campaign, template, cost columns denormalized.
- Standard marts: campaign funnel (delivered→read→replied→converted), template performance, per-number health, cost by category/country/tenant.
- NLQ / text-to-SQL interface over marts = Wave 4 (reuses Quaeris planner/executor learnings; not a v1 promise).
- Warehouse connectors (Parquet export to object storage) for tenants on enterprise plans — Wave 4.

---

## 5. MULTI-TENANCY, RBAC, SECURITY

- **Tenant model:** `tenant → waba(s) → phone_number(s)`; verticals map their own org models (franchise, clinic, salon) onto platform tenants via `external_tenant_ref`.
- **Isolation:** PostgreSQL RLS on every tenant-scoped table (`app.tenant_id` GUC), same enforcement pattern as Laundry Ghar 9-schema design; cross-tenant queries only via platform-admin role with audit.
- **RBAC:** backend-driven, deny-wins, set-based resolution (DocSlot resolver pattern). Platform roles: `platform_admin`, `tenant_admin`, `developer`, `agent`, `analyst`, `billing`.
- **Secrets:** Meta system-user tokens per WABA encrypted at rest (envelope encryption); webhook app secret rotated quarterly; Flows data-exchange private keys in KMS.
- **API keys:** tenant keys hashed (argon2id), prefix-identifiable, scoped (send-only, read-only, admin), IP allowlist optional.
- **Audit:** every admin mutation + every consent event + every template submission → `system.audit_log` (append-only, 160-audit-column convention NOT used here — single audit table + `created_*/updated_*` quartet on domain tables, per Laundry Ghar standard).
- **PII:** wa_id treated as personal data; masked in logs; message bodies excluded from OTel traces.

---

## 6. DATABASE SCHEMA OUTLINE

Database: `waplatform` (PostgreSQL). DDD schema split, ~38 tables v1. Full DDL delivered as versioned SQL files (`V001__…` onward), sqlfluff-clean, validated against real PostgreSQL before merge — standing convention.

| Schema | Tables (v1) |
|---|---|
| `tenancy` | tenants, tenant_settings, api_keys, external_tenant_refs |
| `waba` | business_accounts, phone_numbers, phone_number_events, currency_migrations, business_profiles |
| `templates` | templates, template_versions, template_status_events, template_category_changes, template_lint_results, template_packs |
| `messaging` | outbound_messages, outbound_outbox, inbound_messages, message_statuses, media_assets, suppression_list |
| `sessions` | conversation_windows, window_events |
| `flows` | flow_definitions, flow_versions, flow_responses |
| `billing` | rate_cards, rate_card_entries, message_costs, tenant_quotas, usage_counters, payment_transactions, invoices_feed, max_price_configs |
| `quality` | number_quality_events, messaging_tier_events, guardian_incidents, health_snapshots |
| `consent` | opt_in_events, opt_out_events, erasure_requests, retention_policies |
| `ai` | knowledge_bases, prompts, prompt_versions, conversation_ai_logs, eval_events |
| `analytics` | events (partitioned), campaign_stats_daily, template_stats_daily |
| `ingest` | raw_webhooks (30-day TTL), webhook_dedupe |
| `system` | audit_log, feature_flags, jobs, job_runs |

Key constraints: `message_costs.wamid` unique; `outbound_messages` idempotency-key unique per tenant per 24h (partial index); `conversation_windows` EXCLUDE-style overlap prevention not needed (single active row per pair, upsert); FK audit gate in CI (lesson: 147 missing FKs found in Laundry Ghar audit — never again).

---

## 7. INTEGRATION CONTRACTS (VERTICAL ↔ PLATFORM)

### 7.1 Internal REST (via YARP)

```
POST   /v1/messages                    send (all types)
POST   /v1/campaigns                   broadcast (tier-aware chunking)
GET    /v1/windows/{waId}              window state
POST   /v1/templates                   create + lint + submit
GET    /v1/templates/{id}/status
POST   /v1/flows/{id}/launch
POST   /v1/payments/orders             order_details send
GET    /v1/costs/estimate              pre-send billable estimate
GET    /v1/numbers/{id}/health         Guardian snapshot
POST   /v1/consent/opt-in              record consent w/ evidence
POST   /v1/onboarding/embedded-signup  ES token exchange
```

### 7.2 Events consumed by verticals (RabbitMQ, versioned contracts)

`wa.message.received.v1`, `wa.message.status.v1`, `wa.flow.response.v1`, `wa.payment.status.v1`, `wa.window.closing.v1`, `wa.quality.changed.v1`, `wa.template.status_changed.v1`, `wa.account.alert.v1`

Contract rules: additive-only within a major version; schema registry in `WaPlatform.Contracts`; consumer-driven contract tests in CI.

---

## 8. NON-FUNCTIONAL REQUIREMENTS

| Dimension | Target |
|---|---|
| Webhook ack latency | p99 < 500ms (hard Meta expectation <1s) |
| Outbound dispatch latency | p95 < 2s enqueue→Graph call (excl. Meta) |
| Throughput | 200 MPS aggregate v1; horizontally scalable to 1,000 MPS per number |
| Availability | 99.9% gateway/ingest; degraded mode: ingest never drops (raw persist even if bus down) |
| Durability | Zero message loss: outbox + at-least-once + idempotent consumers |
| Cost ledger accuracy | 100% reconciliation vs Meta invoice, monthly, <0.5% unexplained variance |
| Observability | OTel traces per wamid; RED metrics per service; Guardian SLO dashboards |
| DR | PITR on PostgreSQL; RPO 5 min, RTO 1h v1 |

---

## 9. COMPLIANCE MATRIX

| Regime | Obligation | Platform control |
|---|---|---|
| Meta WhatsApp Business Policy | Opt-in before business-initiated; category integrity; no prohibited industries | Consent ledger; template lint; tenant onboarding vetting checklist |
| Meta Commerce Policy | Payments/orders content rules | order_details validation; vertical category flags |
| Meta AI Providers policy (2026) | TBD per review | OD-1 gate before AI GA |
| DPDP Act 2023 | Consent, purpose limitation, rights, breach notification | Consent ledger, erasure workflow, retention policies, audit log |
| RBI / UPI | Payment flow integrity | UPI-native only v1; PSP-certified fallback; no card storage |
| GST | Invoice trail on tenant billing | invoices_feed with GSTIN fields, HSN/SAC per vertical |
| PCPNDT (DocSlot only) | Healthcare messaging restrictions | Enforced in DocSlot layer; platform exposes content-policy hooks |

---

## 10. BUILD PLAN — WAVES

Wave-based multi-agent execution per standing orchestrator conventions (`.claude/agents/`, `.agents/memory/`, handoff notes, QA live-test loop with bounded 3-attempt fix). **Agents never commit or push — git is manual.**

### Wave 0 — Foundations (blockers)
- Meta app setup, Embedded Signup approval, test WABA + numbers (INR).
- OD-1 AI-provider policy review; OD-2/OD-3 decisions (Section 14).
- `waplatform` DB bootstrap: tenancy, waba, ingest, system schemas (V001–V004).
- Aspire AppHost skeleton, contracts package, CI with sqlfluff + FK-audit gate.

### Wave 1 — Send/Receive Core
- wa-ingest-svc (verify, raw persist, dedupe, normalize, publish).
- wa-gateway-svc (text/template/media/interactive; idempotency; outbox; retries).
- Session Window Manager + window-aware send policy.
- Template lifecycle (CRUD, submit, status webhooks) — lint stub.
- Schemas: messaging, sessions, templates (V005–V008).
- QA: 8-scenario live smoke against real test number.

### Wave 2 — Money & Safety
- Cost & Billing engine: rate cards, webhook-sourced cost ledger, estimator, quotas.
- Quality Rating Guardian: quality/tier ingestion, auto-throttle, health snapshots.
- Consent ledger + suppression enforcement + STOP listener.
- Campaign engine with tier-aware chunking.
- Schemas: billing, quality, consent (V009–V012).
- **Exit gate G1:** DocSlot migrated onto platform core in staging.

### Wave 3 — Rich Channel
- Flows engine + DocSlot booking flow + Laundry Ghar pickup flow.
- UPI payments (order_details) + reconciliation.
- Template policy lint v1 (rules + LLM).
- Max-price bidding config (behind flag until Meta open beta).
- **Exit gate G2:** Laundry Ghar live on platform core.

### Wave 4 — Intelligence
- AI Orchestration gateway (post OD-1 clearance) with eval loop.
- Analytics marts + campaign funnel dashboards; NLQ interface (Quaeris lineage).
- Warehouse export connectors.
- **Exit gate G3:** decision point — open external API product? Requires: 3 verticals in production, Guardian incident rate < threshold, billing reconciliation clean 3 consecutive months, unit-economics model approved.

---

## 11. AGENT TEAM MAPPING (v1 proposal)

| Agent | Scope |
|---|---|
| `wa-schema-agent` | SQL files, migrations, FK audit, RLS policies |
| `wa-backend-agent` | .NET services, CQRS handlers, outbox, EF Core |
| `wa-integration-agent` | Graph API client, webhook contracts, Flows/payments specifics |
| `wa-billing-agent` | Rate cards, cost ledger, reconciliation jobs |
| `wa-security-agent` | DPDP/RBI/Meta-policy enforcement, secrets, RLS review |
| `wa-qa-agent` | Live smoke loop, contract tests, webhook replay harness |
| `wa-docs-agent` | ADRs, runbooks, tenant-facing docs |

Institutional memory in `.agents/memory/`; checkpoints in `.agents/checkpoints/`.

---

## 12. ARCHITECTURE DECISION RECORDS (SUMMARY)

- **ADR-001 — Direct Meta Cloud API, no BSP.** Cloud API is free to access, on-prem deprecated, BSP markup adds cost without capability. Trade-off: we own onboarding UX and policy compliance. ACCEPTED.
- **ADR-002 — Per-message (PMP) billing schema from day one.** Webhook `pricing` object is the billing source of truth; no conversation-based tables anywhere. ACCEPTED.
- **ADR-003 — Vertical-first; external API gated at G3.** Horizontal resale deferred until 3 production verticals + clean economics. ACCEPTED.
- **ADR-004 — Shared platform as separate deployable + separate DB**, not a schema inside each vertical DB. Verticals integrate via API/events only; no cross-DB FKs. ACCEPTED.
- **ADR-005 — Window-aware send rejection** (no silent free-form→template conversion). Cost transparency to verticals over convenience. ACCEPTED.
- **ADR-006 — INR-native WABA provisioning + migration tooling** ahead of 2026-12-31 deadline. ACCEPTED.
- **ADR-007 — Custom CQRS without MediatR** per microservices-architect skill; platform is service-oriented, not the Jason Taylor monolith template. PROPOSED — confirm.

---

## 13. RISKS

| Risk | Likelihood | Impact | Mitigation |
|---|---|---|---|
| Meta policy/pricing shifts (quarterly) | High | Med | Rate-card versioning, quarterly review job, ≥1-quarter notice window monitored |
| AI Providers policy restricts our AI layer | Med | High | OD-1 gate; AI features flag-gated; deterministic-flow fallback |
| Number quality bans on tenant misuse | Med | High | Guardian auto-throttle; onboarding vetting; template lint; suppression enforcement |
| INR migration missed on legacy WABA | Low | Critical (delivery stops 2027-01-01) | Migration API tooling Wave 0; hard alerts from 2026-10 |
| Marketing rate inflation (India +10% Jan 2026, more likely) | High | Med | Utility-first flow design; inbound-first mandate; window maximization |
| Scope creep toward horizontal CPaaS | High | High | G3 gate is explicit; this document is the contract |

---

## 14. OPEN DECISIONS

- **OD-1:** Meta "AI Providers" pricing policy — legal review outcome; does our AI gateway classify us as an AI Provider? **Blocker for Wave 4.**
- **OD-2:** Laundry Ghar number strategy — one number per franchise outlet vs shared number + outlet routing. Cost, tier, and green-tick implications differ.
- **OD-3:** Confirm ADR-007 (custom CQRS vs MediatR) — align with existing DocSlot codebase direction before Wave 1.
- **OD-4:** Marketing Messages Lite API adoption (Meta's lightweight marketing send path) — evaluate vs standard Cloud API sends for campaign engine.
- **OD-5:** Tenant-visible channel pricing — pure pass-through of Meta cost vs bundled quota in subscription tiers (recommendation: bundled quota, pass-through overage).

---

*End of specification. Companion deliverables on approval: full DDL (V001–V012), OpenAPI spec for §7.1, event contract schemas, ADR long-forms, agent operating manuals.*
