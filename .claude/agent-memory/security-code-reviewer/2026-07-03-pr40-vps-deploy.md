---
name: 2026-07-03-pr40-vps-deploy
description: Security audit of PR #40 (VPS deployment baseline, issue #12) — verdict APPROVE, no blocking findings; Should-fix list gated on "before first real VPS deploy"
metadata:
  type: project
---

# PR #40 security audit (2026-07-03) — VPS deployment baseline

Verdict: APPROVE (no exploitable/blocking findings). 14/14 crypto tests pass (ran in isolated worktree). No age private key or plaintext prod.env/.pem anywhere in branch history (verified with `git log --diff-filter=AD`). Committed `.enc` files are single-recipient (the documented throwaway demo key), undecryptable by anything in the repo; ciphertext lengths consistent with placeholder values.

## Should-fix (most gate on "before first real VPS deploy", not merge)
**Update 2026-07-06**: JWT knob, ssh-action pin+fingerprint, encrypt.sh guard, and migrator argv were fixed via issue #42 and re-verified PASS — see [[2026-07-06-issue42-reverify]]. Still open from this list: ufw/Docker bypass doc, backup umask/restore-drill guard, AesGcm AAD.
- **JWT breaks in prod compose**: services set `Jwt__Authority: http://core:8080` but Program.cs sets `RequireHttpsMetadata = !IsDevelopment()` → metadata fetch throws in Production on first authenticated request. Fails closed (not a vuln) but invites a blind `RequireHttpsMetadata=false` workaround. Local verification (healthchecks = anonymous 404s) can't catch it.
- **setup-ufw.sh comment is misleading**: `ufw deny 5432/5672/15672` claims to guard against future compose `ports:` edits — false; Docker-published ports DNAT in PREROUTING/DOCKER chain and bypass ufw INPUT entirely. Document DOCKER-USER / 127.0.0.1-bind convention instead.
- **appleboy/ssh-action@v1** — mutable tag receiving VPS_SSH_KEY; no host-key verification by default (needs `fingerprint:` input). Pin by SHA + set fingerprint before VPS secrets are configured.
- **Backup scripts**: no `umask 077` → dumps land 644 in a 755 dir (full-DB PII world-readable to local users); restore_drill has no `SCRATCH_DB != POSTGRES_DB` guard before `DROP DATABASE ... WITH (FORCE)`.
- **encrypt.sh footgun**: no guard against encrypting real secrets to the demo recipient still in .sops.yaml (result: unrecoverable data, or trust in an unverifiable key-deletion claim). One-line grep guard suggested.
- **Migrator superuser password in argv** (`command:` list → world-readable /proc/cmdline on host during runs).
- **AesGcmEnvelopeCipher: no AAD / context binding** — envelope prefix unauthenticated, ciphertexts swappable across rows by a DB writer. Add optional AAD param BEFORE Wave 1 consumers wire it (breaking change later). Otherwise solid: AES-256-GCM, random nonces (data key single-use), 16-byte tag, versioned format, wrong-key/tamper/rewrap tests all meaningful.

## Verified-good (don't re-flag)
- deploy.yml: `permissions: contents: read, packages: write` least-privilege; no pull_request trigger (no PR-code path to secrets); skip-when-no-secrets logic is a single gated step, can't half-run.
- Compose: only Caddy publishes host ports; postgres/rabbitmq/gateway internal-only; migrator behind `profiles: ["tools"]` (never starts on `up`, not remotely triggerable); non-root `USER wavio` in app images; `restart: unless-stopped`.
- app_user prod bootstrap (init-prod script) creates the role with real password before V001's weak dev fallback; NOSUPERUSER/NOBYPASSRLS; no Admin conn string in any running service.
- decrypt.sh: umask 077, temp-file+mv (no half-written output), chmod 600. Caddy: no admin API exposed, HSTS, -Server.
- Nits: PII_ENCRYPTION_KEY handed to wavio.Gateway which never touches WavioDbContext (least-privilege); rabbit creds in URL break on special chars; unpinned base-image tags; secrets interpolated into workflow shell conditionals (prefer env:); FormatException vs CryptographicException inconsistency on malformed base64 envelope.

See [[wavio-security-conventions]] and [[2026-07-03-pr41-ingest-webhooks]] (PR #41 depends on this stack's RabbitMQ/secrets posture).
