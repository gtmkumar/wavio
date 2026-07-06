# Meta App Setup Runbook (issue #6)

Step-by-step for the human-only parts of issue #6, mapped to exactly where each
value lands in this codebase. The platform side is ready: webhook handshake +
signature verification are implemented and fail closed (`WaIngest.WebApi/Endpoints/Webhooks.cs`),
the Graph send/template clients bind `Meta:Graph` (`WaGateway`/`WaAdmin`
`Infrastructure/Graph/MetaGraphOptions.cs`), and production plumbing for all
three secrets exists in `docker-compose.prod.yml` + `deploy/secrets/prod.env.example`.

**Order matters**: the webhook callback URL must be live (HTTPS, publicly
reachable) before Meta will accept it — so the app + secrets come first, then a
deploy, then the webhook configuration.

## 1. Create the Meta developer app

1. <https://developers.facebook.com> → My Apps → Create App → type **Business**.
2. Add the **WhatsApp** product to the app.
3. App settings → Basic → copy the **App Secret** → this is
   `META_WEBHOOK_APP_SECRET` in `deploy/secrets/prod.env` (never anywhere else —
   see `deploy/secrets/README.md` for the age-encryption workflow).

## 2. Create the system-user token

1. Business Manager (<https://business.facebook.com>) → Business settings →
   Users → **System users** → create an *Admin* system user.
2. Assign the app + the WABA (step 3) to it with full control.
3. Generate a token with **`whatsapp_business_messaging`** and
   **`whatsapp_business_management`** permissions. Prefer a non-expiring token
   (60-day tokens will silently kill sends when they lapse).
4. Token → `META_GRAPH_ACCESS_TOKEN` in `prod.env`.
   - **Storage**: age-encrypted `prod.env.enc` in this repo is the only
     persisted copy (envelope encryption per issue #12 tooling). It reaches the
     services only as an env var; it is never logged (`MetaGraphOptions.AccessToken`
     is documented never-logged and no logger touches it).
   - **Rotation**: generate a new token in Business Manager → update `prod.env` →
     `encrypt.sh` → commit → redeploy → revoke the old token. Old token stays
     valid until revoked, so rotation is zero-downtime.
   - This single token is a stopgap: per-WABA envelope-encrypted token storage
     ships with Embedded Signup onboarding (this issue's ES task).

## 3. Test WABA + INR phone number

1. WhatsApp → API Setup in the app dashboard gives a test WABA + test number
   automatically — fine for the round-trip test below.
2. For the real WABA: create it under the Business Manager, add a phone number,
   and **provision it INR-billed from day one** (ADR-006; all WABAs must be INR
   by 2026-12-31, Meta stops delivery from non-INR WABAs 2027-01-01).
   Billing localization for eligible Indian customers has been live since
   2026-01-01 — set payment currency to INR when attaching the payment method.

## 4. Deploy, then configure the webhook

1. Generate the verify token yourself: `openssl rand -hex 32` →
   `META_WEBHOOK_VERIFY_TOKEN` in `prod.env`. Re-encrypt, commit, deploy
   (`deploy/README.md` "Production"). All three `META_*` vars are required —
   compose refuses to start without them.
2. App dashboard → WhatsApp → Configuration → Webhook:
   - **Callback URL**: `https://<WAVIO_DOMAIN>/ingest/api/v1/webhooks/meta`
     (Caddy → wavio-gateway `/ingest` prefix → wa-ingest-svc).
   - **Verify token**: the same string you put in `prod.env`.
   - Save — Meta fires the GET handshake (`hub.mode=subscribe`,
     `hub.verify_token`, `hub.challenge`); wa-ingest echoes the challenge only
     on a constant-time token match.
3. **Subscribe to webhook fields** (WhatsApp → Configuration → Webhook fields):
   `messages`, `message_template_status_update`,
   `message_template_quality_update`, `phone_number_quality_update`,
   `phone_number_name_update`, `account_update`, `account_alerts`,
   `business_capability_update`. (`messages` carries inbound messages AND
   delivery statuses including the `pricing` object — ADR-002's billing source
   of truth. The quality/tier fields feed WaIntel's Guardian.)

## 5. Verify end-to-end (acceptance criteria)

Handshake (should return `test123`, HTTP 200):

```bash
curl "https://<domain>/ingest/api/v1/webhooks/meta?hub.mode=subscribe&hub.verify_token=<token>&hub.challenge=test123"
```

Signature rejection (should return 401 — unsigned deliveries are never parsed):

```bash
curl -X POST "https://<domain>/ingest/api/v1/webhooks/meta" -H 'Content-Type: application/json' -d '{}'
```

Send round-trip from a dev machine (test number, template `hello_world` ships
pre-approved on test WABAs):

```bash
curl -X POST "https://graph.facebook.com/v21.0/<PHONE_NUMBER_ID>/messages" \
  -H "Authorization: Bearer $META_GRAPH_ACCESS_TOKEN" \
  -H 'Content-Type: application/json' \
  -d '{"messaging_product":"whatsapp","to":"<recipient>","type":"template","template":{"name":"hello_world","language":{"code":"en_US"}}}'
```

Then reply from the recipient phone and confirm the inbound message lands in
`ingestion.raw_webhooks` with `signature_valid = true` and a normalized event is
published (wa-ingest logs / RabbitMQ).

## 6. Embedded Signup approval

Apply for Embedded Signup (App Dashboard → WhatsApp → Embedded Signup) — the
only tenant onboarding path per spec §4.1. Requirements to have ready: verified
Business Manager, privacy policy URL on the app, app review submission
describing the onboarding flow. Approval lags — submit now, track status on
issue #6.
