# Open Decisions — Resolution Records (issue #7)

Resolves the spec §14 open decisions. Each record follows the ADR summary style of
spec §12. Statuses: **ACCEPTED** (decided, in force), **RATIFY** (engineering
recommendation recorded; owner sign-off closes it), **DEFERRED** (position noted,
decision intentionally postponed to a named trigger).

Recorded 2026-07-06. Link this file from issue #7 when closing it.

---

## OD-1 — Meta "AI Providers" policy: does the AI gateway make us an AI Provider?

**Status: RATIFY (engineering position; legal/owner sign-off required before Wave 4 GA — Wave 4 *build* is unblocked)**

### Policy facts (verified 2026-07-06 against developers.facebook.com)

- Meta's ToS (2026-01-15) defines AI Providers as: *"Providers and developers of
  artificial intelligence or machine learning technologies, such as large language
  models, generative artificial intelligence platforms, general-purpose artificial
  intelligence assistants, or similar technologies who provide certain services on
  WhatsApp Business Platform."*
- The pricing consequence was per-message charges on **non-template messages sent by
  AI Providers in regulated markets** (eff. 2026-02-16; Brazil/EU variants Mar–May).
  **As of 2026-05-13 Meta discontinued these charges in most markets; only Brazil
  currently remains charged.** India is not, and never was, a charged market under
  this policy.
- Billing surface: Meta added an `AI_BOT` pricing category and a
  `general_purpose_ai` webhook classification. Both arrive through the webhook
  `pricing` object — which ADR-002 already makes our billing source of truth.
- Adjacent (separate) change to watch: Meta Business Agent (Meta's own AI) moves to
  token-based pricing 2026-08-01, and service-message charging changes are being
  discussed for late 2026. Neither changes our schema: PMP rows key off whatever
  category the webhook stamps.

### Position

Wavio's AI orchestration gateway is **business-specific automation on each tenant's
own WABA** (appointment booking, pickup scheduling, order status — deterministic
flows with LLM assist), not a *general-purpose AI assistant* offered to consumers.
The policy's named exemplars — LLM platforms, general-purpose assistants — and its
`general_purpose_ai` webhook classification target ChatGPT-style bots on WhatsApp.
Engineering position: **Wavio is not an AI Provider under the current policy text.**

The definitional language is broad enough that Meta could read "developers of …
similar technologies who provide certain services" expansively, so the position is
defended by construction, not assumption:

1. **Billing is reclassification-proof.** Costs key off the webhook `pricing`
   object (ADR-002). If Meta ever stamps our traffic `AI_BOT` /
   `general_purpose_ai`, real costs flow into `message_costs` with zero code
   change — we'd see it in reconciliation immediately, not in a surprise invoice.
2. **No general-purpose assistant behavior.** Wave 4 system prompts are scoped to
   the tenant's business domain; open-domain chat is refused by the guardrail layer
   (spec Wave 4 "guardrails, eval loop"). This keeps us outside the exemplar list.
3. **Flag-gated GA.** AI features ship behind per-tenant flags (existing risk-register
   mitigation); the flag stays off in any market Meta lists as charged (today:
   Brazil — not a target market).
4. **Quarterly policy re-review** (existing risk-register item) re-checks the policy
   page each rate-card refresh; the 2026-05-12 update proved this policy moves.

### Consequence

Wave 4 (#30 AI Orchestration gateway) is **buildable now** under constraints 1–3.
GA additionally requires owner/legal sign-off on this record.

---

## OD-2 — Laundry Ghar number strategy: per-outlet numbers vs shared number + outlet routing

**Status: RATIFY (recommendation recorded; reversible either way — schema and Guardian support both)**

### Decision

**Default: one shared brand number per franchise brand, with outlet routing.
Escalate individual outlets to dedicated numbers only when volume or quality
isolation demands it.**

### Rationale

- **Tier ramp concentrates.** Messaging-tier headroom (TIER_250 → 1K → 10K → …) is
  per phone number. One shared number pools all outlets' volume and climbs tiers
  fast; thirty per-outlet numbers each crawl from 250/day, throttling every
  outlet's marketing for weeks.
- **Green tick / display name** review is per number. One shared number means one
  Official Business Account request (spec §4.1 workflow) for the brand, not one
  per outlet.
- **Cost:** Cloud API numbers are free to hold, so per-outlet is not a fee problem —
  it is an operational one: N× display-name reviews, N× quality scores to watch,
  N× registration ceremonies, and the default 20-numbers-per-WABA cap.
- **The real per-outlet argument is blast radius:** on a shared number, one outlet's
  spammy behavior drops quality for the whole brand and Guardian (WaIntel) throttles
  everyone. Mitigations: template lint (#27), suppression enforcement, and Guardian's
  YELLOW/RED gates already contain the damage; if an outlet is persistently risky or
  crosses ~thousands of marketing sends/day on its own, move *that outlet* to a
  dedicated number.
- **Reversibility:** spec §4.1 already supports multi-number per WABA ("per-vertical
  decision"), `waba.phone_numbers` models N numbers per tenant, and Guardian tracks
  quality per number. Starting shared and splitting hot outlets later is a
  configuration change, not a migration.

### Consequence

Laundry Ghar onboarding (G2) provisions one INR-billed brand number; the vertical
carries outlet identity in message content/metadata (outlet routing is the
vertical's concern per ADR-004 — the platform sees one phone_number_id). Revisit
per-outlet escalation when a single outlet sustains marketing volume near the
brand number's tier headroom or Guardian repeatedly flags it.

---

## OD-3 — Confirm ADR-007: custom CQRS dispatcher, no MediatR

**Status: ACCEPTED (spec §12 ADR-007 updated PROPOSED → ACCEPTED)**

Confirmed by shipped code, not preference: the custom dispatcher
(`wavio.Utilities/CQRS`) has been the production pattern since Wave 1 across all
five services + core — every command/query in WaGateway, WaIngest, WaAdmin,
WaBilling, WaIntel and ~530 green tests run through it. No MediatR package
reference exists anywhere in the solution. This matches the DocSlot codebase
direction (custom dispatcher, service-oriented — not the monolith-template stack).
Reversing now would be a rewrite with no capability gain; MediatR's v12+ licensing
change is a further point against. **Closed.**

---

## OD-4 — Marketing Messages Lite API (non-blocking position)

**Status: DEFERRED (re-evaluate after G2 with real campaign volume data)**

The campaign engine (#22) dispatches through the standard Cloud API send path
(`SendMessageCommand` → outbox → Graph). MM Lite is a send-path optimization, not a
capability gap — and adopting it early would fork the dispatch pipeline before we
have volume data to justify it. The engine's design keeps the seam: campaigns fan
out through the same accept path as single sends, so an MM Lite adapter would slot
in at the Graph client without touching campaign/chunker logic. Trigger to revisit:
sustained marketing volume post-G2 where MM Lite's delivery/pricing advantages are
measurable against our own PMP data.

---

## OD-5 — Tenant-visible channel pricing (non-blocking position)

**Status: ADOPTED as the working position for billing-plan design**

**Bundled quota in subscription tiers + transparent pass-through overage** (the
spec's own recommendation, now on record). The billing engine already computes
exact per-message Meta cost from the webhook `pricing` object (ADR-002) and the
estimator prices sends in advance, so plans are a commercial wrapper over known
COGS: each tier includes N conversation-equivalents/messages; overage bills at
Meta cost + stated margin. Pure pass-through is rejected (exposes tenants to Meta's
quarterly volatility and makes plans uncomparable); pure bundled-unlimited is
rejected (marketing blasts would invert unit economics). Plan tables arrive with
the billing-plans work; nothing in the current schema constrains the choice.
