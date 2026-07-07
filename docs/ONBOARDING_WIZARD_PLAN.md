# WhatsApp Onboarding Wizard — Concept Plan

Guided, step-by-step WhatsApp onboarding inside the Wavio Console (spec §4.1: Embedded
Signup as the only onboarding path — "no manual Business Manager gymnastics for tenants").
Built **concept-first against `tools/MetaGraphApiStub`** so the whole flow is clickable in dev
today; flipping to real Meta later is a config change, not a rewrite. Serves two audiences with
one wizard: us (the platform's first WABA) and every customer tenant onboarding later.

## What Meta lets us automate vs. not

| In our UI (Graph API) | Stays with Meta (wizard tracks + explains, cannot skip) |
|---|---|
| Embedded Signup popup launch + token exchange | Facebook login inside the popup (credentials never touch us) |
| WABA + phone discovery, persist to `waba.*` | Meta developer app creation + ES approval (docs/META_APP_SETUP.md, issue #6, one-time, human) |
| Phone registration (2-step PIN), OTP request/verify | Business verification review (days–weeks, Meta decides) |
| Display name set, business profile get/set | Display-name review approval |
| Webhook subscribe (`subscribed_apps`) | Official Business Account "green tick" review |
| Status polling: name review, verification, quality, tier | |

## Flow (wizard steps)

```
Step 1 CONNECT      "Connect WhatsApp" → ES popup (stub mode: simulated popup returns a
                    fake code) → POST onboarding/embedded-signup → token exchanged,
                    WABA + numbers persisted, webhooks subscribed automatically.
Step 2 NUMBER       Pick/confirm phone number → set 2-step PIN → register. If the number
                    needs OTP verification: request code → enter code.
Step 3 PROFILE      Display name + business profile (about, description, address, email,
                    websites, photo URL) → saved to Meta and waba.business_profiles.
Step 4 CHECKS       Live checklist: connected / registered / profile set / webhooks on /
                    name review / business verification / quality + tier. Green = ready
                    to send. Ambers explain in plain language what Meta is still reviewing.
```

The wizard is resumable: every step derives its state from the DB + a Graph status refresh,
so closing the browser mid-flow loses nothing.

## Phase 1 — Stub extensions (`tools/MetaGraphApiStub`, dev-only)

Add the Graph endpoints the onboarding client will call (same shapes as real Meta v23.0):

- `POST /{v}/oauth/access_token` — ES code → fake business token
- `GET  /{v}/{wabaId}` + `GET /{v}/{wabaId}/phone_numbers` — WABA info + numbers
- `POST /{v}/{wabaId}/subscribed_apps` — webhook subscribe (always succeeds)
- `POST /{v}/{phoneId}/register` — accepts `{pin}`; flips stub state to registered
- `POST /{v}/{phoneId}/request_code` / `verify_code` — OTP simulation (code `000000`)
- `GET/POST /{v}/{phoneId}/whatsapp_business_profile` — profile get/set (in-memory)
- `GET  /{v}/{phoneId}?fields=name_status,code_verification_status,quality_rating,...`
  — status polling; stub advances `name_status` PENDING → APPROVED after ~30s so the
  "waiting on Meta review" UX is demonstrable.

## Phase 2 — Backend (wa-admin-svc)

Reuse the existing pattern exactly: `MetaGraphOptions` + typed HttpClient (like
`MetaGraphTemplateClient`), CQRS handlers, envelope responses, RLS-scoped writes.

New endpoints under `/admin/v1/onboarding` (spec route `POST /v1/onboarding/embedded-signup`):

- `POST embedded-signup` — exchange code → business token (encrypted with the existing
  `IFieldCipher` into `business_accounts.system_user_token_ciphertext` — the per-WABA token
  storage META_APP_SETUP.md already calls for); fetch + upsert WABA and phone numbers;
  subscribe webhooks; return snapshot.
- `GET  status` — the step-4 checklist (DB state + on-demand Graph refresh).
- `POST phone-numbers/{id}/register` — body `{pin}`.
- `POST phone-numbers/{id}/request-code` / `verify-code` — OTP path.
- `GET/PUT phone-numbers/{id}/profile` — business profile (Graph + `waba.business_profiles`).
- `POST refresh` — re-pull review/quality statuses from Graph.

**Migration `V014__onboarding.sql`** (small, additive): review-status columns that have no
home yet — `business_accounts.verification_status`, `phone_numbers.name_status`,
`phone_numbers.code_verification_status`, `phone_numbers.registered_at`. No new tables;
wizard progress is derived, not stored.

**Permissions**: new `waba.onboarding.manage` (High risk → step-up OTP guard applies
automatically via the existing ScopeResolver rule), seeded alongside existing permission rows.

## Phase 3 — Console wizard (admin-web)

- New route `/onboarding` — full-page stepper (not a slide-over: it's a journey, not an edit).
  Sidebar entry "Get started" gated on `waba.onboarding.manage`, badge until checks are green.
- Step components with the standing conventions: friendly inputs only (no JSON), amber
  plain-language wait states, FieldErrors on every input, step-up dialog reuse.
- Stub mode: "Connect (simulated)" button replaces the FB SDK popup. Real mode later loads
  the FB JS SDK with the app id from config — the only frontend piece that changes.

## Phase 4 — Proof

- Backend: handler tests (WaAdmin.Tests) + one integration test for the embedded-signup
  upsert (real Postgres, RLS on).
- Stub: exercised end-to-end by driving the wizard live (reticle) — connect → register →
  profile → all checks green; DB verified via psql.
- Existing suites stay green; typecheck/lint clean.

## Flip-to-real checklist (after issue #6 is done by a human)

1. `Meta:Graph:BaseUrl` → `https://graph.facebook.com` + real app secret/token env vars
   (plumbing already exists in prod compose).
2. Console gets the FB app id + ES config id; simulated connect button swaps for the SDK popup.
3. Re-run the wizard against a real test WABA (issue #18/#24 staging).

## Out of scope (deferred, per product-scope decision)

Green-tick (OBA) request workflow, currency-migration tooling UI, multi-number franchise
routing — spec'd, but not part of the concept slice.
