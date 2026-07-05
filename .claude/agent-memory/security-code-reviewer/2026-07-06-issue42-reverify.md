---
name: 2026-07-06-issue42-reverify
description: Re-verification of issue #42 pre-first-deploy hardening (S1/S3/S5/S6 + 3 nits from PR #40 audit) — all 7 PASS, verdict APPROVE-with-notes; 1 new Low (fingerprint fail-open when secret unset)
metadata:
  type: project
---

# Issue #42 re-verification (2026-07-06) — uncommitted on `develop`

All four Should-fixes (S1/S3/S5/S6) and three nits from [[2026-07-03-pr40-vps-deploy]] verified PASS against the actual diff, not the implementer's notes. Verdict: **APPROVE-with-notes**.

## What was independently verified (methods worth reusing)
- **S1**: nullable-coalesce knob `GetValue<bool?>("Jwt:RequireHttpsMetadata") ?? !IsDevelopment()` in all 5 wa-* WebApi hosts — fail-closed when unset; no appsettings*.json sets it anywhere (grep), so dev unchanged. Rendered `docker compose --profile tools config` proves the `"false"` override lands on exactly the 5 wa-*-svc blocks; core and wavio-gateway untouched. core.WebApi confirmed in-process validation (`IssuerSigningKey = keyProvider.SigningKey`, no `Authority`) — S1 genuinely N/A there.
- **S3**: `gh api repos/appleboy/ssh-action/git/ref/tags/{v1,v1.2.5}` → both = `0ff4204d59e8e51228ff73bce53f80d53301dee2`, matches the pin. Fingerprint input wired + operator instructions in-line.
- **S5**: guard literal = exact recipient in `.sops.yaml`; tripped it live from `/private/tmp` cwd against a scratch file → exit 1, no `.enc`, only the *public* key in output. `SCRIPT_DIR` via `BASH_SOURCE` makes it cwd-proof.
- **S6**: rendered compose config shows migrator `command:` = `--migrations-dir` only, superuser string solely in `environment:`; `wavio.DbMigrator/Program.cs:109` reads `ConnectionStrings__Admin` (2nd priority after CLI arg). Repo-wide grep: no other prod superuser string; remaining `Username=postgres` hits are dev-localhost defaults only.
- **Nits**: PII key on core + 5 wa-*-svc only (wavio-gateway YARP proxy has no AddAuthentication/DbContext/Pii surface at all — grep of its Program.cs came back empty); every `${{ secrets.* }}` in deploy.yml is in `env:`/`with:` (only `${{ github.sha }}` — hex, safe — appears in a script); all 5 image digests confirmed to exist upstream via `docker manifest inspect`.
- Build 0 errors; 526/526 tests re-run and passed.

## New findings (this pass)
1. **Low**: `VPS_HOST_FINGERPRINT` is NOT part of the "Check VPS secrets are configured" gate (deploy.yml only gates HOST/USER/KEY). If the operator sets the 3 gated secrets but forgets the fingerprint, ssh-action gets an empty `fingerprint:` and **silently skips host-key verification** (confirmed: README at the pinned SHA — fingerprint has no default). Fix: add it to the gate's env+if check.
2. **Informational**: S6 comment implies `environment:` avoids `docker inspect` — env is still in `docker inspect` Config.Env for socket-holders; the real win is world-readable `/proc/<pid>/cmdline` vs owner-only `/proc/<pid>/environ`. Fix correct, comment slightly overstates.
3. **Informational**: `docker-compose.dev.yml` still uses mutable `postgres:16`/`rabbitmq:3-management` tags (local dev only, acceptable).
4. **Informational**: S5 guard is skipped if `.sops.yaml` is deleted (`[ -f ]` gate) — then sops itself fails without creation rules, so fail-safe in practice.

Lesson: `docker compose config` needs ALL `:?`-required vars as dummies (WAVIO_DOMAIN/ACME_EMAIL too, not just the secrets) or it renders nothing.
