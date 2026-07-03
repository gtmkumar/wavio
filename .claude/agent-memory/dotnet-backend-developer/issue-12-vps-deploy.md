---
name: issue-12-vps-deploy
description: What was built for issue #12 (VPS deployment baseline), real bugs found during verification, and what's genuinely user-side
metadata:
  type: project
---

Issue #12 (2026-07-03), branch `feature/12-vps-deploy` (based on
`feature/10-db-migrations` — needs `db/migrations` to exist; not stacked on
#11 since CI isn't a runtime dependency of the deploy artifacts themselves).

## Files
- `src/backend/wavio/Dockerfile` — one shared multi-stage Dockerfile for all 8
  images (7 services + `wavio.DbMigrator`), parameterized by `--build-arg
  PROJECT_PATH`/`ASSEMBLY_NAME`, instead of 8 near-identical copies.
- `src/backend/wavio/.dockerignore` — keeps `bin/`/`obj/`/dev secrets out of
  the build context.
- `docker-compose.prod.yml` (repo root) — postgres, rabbitmq, core, the 5
  wa-*-svc, wavio-gateway, caddy, plus a `migrator` service gated behind
  `profiles: ["tools"]` (one-shot, never starts with plain `up`). Only caddy
  publishes host ports.
- `deploy/caddy/Caddyfile` — reverse proxy, `{$WAVIO_DOMAIN}` for ACME.
- `deploy/postgres/init-prod/001-create-app-role.sh` — prod-only Postgres
  bootstrap (see "app_user password" below).
- `deploy/db-backup/pg_dump_nightly.sh` + `restore_drill.sh` — nightly dump +
  rotation, and a scratch-DB restore verification. **Named `db-backup`, not
  `backup`** — see the gitignore bug below for why.
- `deploy/secrets/` — `.sops.yaml` (repo root) + `prod.env.example` +
  `encrypt.sh`/`decrypt.sh` + `README.md`. SOPS + age, no cloud KMS.
- `deploy/vps/setup-ufw.sh` — firewall baseline (80/443/SSH only).
- `.github/workflows/deploy.yml` — build+push all 8 images to GHCR on push to
  `main`; VPS rollout itself is a manual `workflow_dispatch` (needs
  `VPS_HOST`/`VPS_USER`/`VPS_SSH_KEY` secrets that don't exist yet).

## Real bugs found during verification (would have shipped broken otherwise)

1. **Docker `ENTRYPOINT` silently swallowed CLI args.** Original:
   `ENTRYPOINT ["sh", "-c", "exec dotnet ${ASSEMBLY_NAME}.dll"]`. Any args
   passed at `docker run <image> <args>` (e.g. `wavio.DbMigrator
   --connection-string ... --migrations-dir ...`) never reached `dotnet` —
   Docker appends `docker run`'s trailing args as the shell's positional
   params (`$1`, `$2`, ...), but the script string never referenced `$@`, so
   they were just dropped. `wavio.DbMigrator` silently fell back to its
   default connection string instead of erroring, which is the dangerous part
   — this could have deployed against the wrong database with no visible
   error. Fixed: `ENTRYPOINT ["sh", "-c", "exec dotnet ${ASSEMBLY_NAME}.dll
   \"$@\"", "--"]` (trailing `--` becomes `$0`, real args become `$1...`,
   `"$@"` forwards them). Caught by actually running the containerized
   migrator with real args and checking its own log line ("Target:
   localhost:5432" when I'd passed `Host=postgres`) — would NOT have been
   caught by just checking the build succeeded.
2. **`wavio.DbMigrator`'s directory auto-detection can't find `db/migrations`
   inside its own container** — the Docker build context is
   `src/backend/wavio`, which has no `db/` sibling once packaged. Fixed by
   bind-mounting `./db/migrations:/db/migrations:ro` at *run* time (in the
   compose `migrator` service) and passing `--migrations-dir /db/migrations`
   explicitly, rather than baking `db/` into the image (keeps the generic
   Dockerfile generic).
3. **`deploy/backup/` silently vanished from `git status`.** macOS's
   case-insensitive default filesystem (APFS) makes `core.ignorecase=true` the
   git default, so the pre-existing VisualStudio.gitignore boilerplate rule
   `Backup*/` (meant for old VS-project-conversion folders) matched my
   lowercase `backup/` too. Renamed the directory to `deploy/db-backup/` (a
   name that doesn't start with "backup" at all, case-insensitively) rather
   than fighting the existing rule. Caught by noticing `git status` showed
   `deploy/caddy/` etc. as untracked but NOT `deploy/backup/` despite the
   files being physically present — `git check-ignore -v` confirmed the
   culprit line number.

## A serious near-incident: shared git working directory

Mid-task, `git branch --show-current` had silently changed (another
teammate's concurrent `git checkout` commands moved the shared HEAD — we are
NOT in isolated worktrees, just one physical checkout). Found the ingest
agent's (issue #13) uncommitted work sitting in my working tree and my own new
files landed on their branch. Recovered by: diffing every modified/untracked
path to determine ownership by content (not by assumption), copying my files
to `/tmp` before touching git state, reverting only the confirmed-mine hunk of
`.gitignore`, deleting only confirmed-mine untracked paths, then creating
`feature/12-vps-deploy` fresh off `feature/10-db-migrations` and restoring my
files there. Verified the other agent's state was byte-identical before and
after. **Flagged to the orchestrator** — recommended isolated worktrees
(`EnterWorktree`/`ExitWorktree` tool exists) for future waves. Lesson: after
any long-running background task in a shared checkout, re-check
`git branch --show-current` and `git status` before trusting file locations —
don't assume the branch you started on is still checked out.

## Design decisions
- One shared `Dockerfile` + build args, not 8 copies — same reasoning as
  [[issue-11-ci-pipeline]]'s FK-audit-allowlist-as-data-file choice: single
  point of maintenance beats copy-paste.
- `migrator` is a compose service gated by `profiles: ["tools"]`, not a
  separate `docker run` invocation outside compose — keeps it discoverable
  (`docker compose config --services`) and gets the same network/env
  conventions as everything else, while never starting with plain `up`.
- No `ConnectionStrings:Admin` anywhere in `docker-compose.prod.yml` — no
  running service needs superuser access (seeding is Development-only by
  existing app design), only the one-shot `migrator` does, via
  `--connection-string` passed directly in its `command:`.
- `Jwt:PrivateKey` ships as its own file (`Jwt__PrivateKeyPath`), not an
  inline `.env` line — multi-line PEM text doesn't survive `KEY=VALUE` `.env`
  parsing reliably. Encrypted as a second SOPS-managed file
  (`deploy/secrets/jwt-private-key.pem.enc`), separate from `prod.env.enc`.
- Health checks use `curl -s -o /dev/null http://127.0.0.1:8080/` (any HTTP
  response, even 404, counts as healthy) rather than hitting `/health`/`/alive`
  — those routes are **Development-only by existing design**
  (`wavio.ServiceDefaults/Extensions.cs`, deliberate security consideration
  already in the codebase) — did not touch that gate to make prod health
  checks "nicer"; respected the existing boundary instead.

## Verified locally (no real VPS/domain available — see db-backup below)
- All 8 images build clean (confirmed with `--no-cache` after the branch
  chaos, to rule out any stale-layer contamination — see verification log for
  exact image digests/entrypoints inspected).
- Full compose dependency chain (postgres → migrator → core → wa-*-svc →
  wavio-gateway) boots and reports healthy with locally-tagged images
  standing in for GHCR.
- `deploy/postgres/init-prod/001-create-app-role.sh`: confirmed it creates
  `app_user` with the real (strong) password before migrations run, and that
  V001's own idempotent bootstrap then correctly skips it — logged in as
  `app_user` with the strong password to prove it, and confirmed the weak dev
  default (`app_user`/`app_user`) does NOT work against this database.
- Caddy: `caddy validate` on the real Caddyfile (clean, no warnings after
  removing a redundant `header_up X-Forwarded-Proto` — reverse_proxy sets it
  automatically); then a full reverse-proxy smoke test with `tls internal`
  (self-signed) proxying to the real `wavio.Gateway` image — response showed
  `server: Kestrel` + `via: 1.1 Caddy`, proving actual pass-through, not just
  config parsing. Real Let's Encrypt issuance needs `WAVIO_DOMAIN` to resolve
  to a real public VPS IP — genuinely can't test that here.
- `deploy/db-backup/pg_dump_nightly.sh` + `restore_drill.sh`: ran for real
  against the live dev Postgres (dump → restore into a scratch DB → per-schema
  row-count verification) — passed.
- SOPS/age: full round-trip (encrypt → decrypt → diff, byte-identical) for
  both a dotenv file and a binary PEM, using a throwaway age key generated,
  used, and discarded (never committed). Confirmed decrypt fails loudly
  (nonzero exit, no half-written file) without the right key — fixed a
  leftover-empty-file rough edge in `decrypt.sh` found during that test
  (write to temp, `mv` into place only on success).
- `deploy/vps/setup-ufw.sh`: actually **run** (in a `--privileged` container,
  not just read) — final `ufw status verbose` output matches the intended
  ruleset exactly.

## Genuinely user-side (documented, not faked)
- Real age keypair generation + getting the private half onto the VPS.
- Real Let's Encrypt cert issuance (needs `WAVIO_DOMAIN` → real public IP).
- `VPS_HOST`/`VPS_USER`/`VPS_SSH_KEY` GitHub secrets for the `ssh-deploy` job.
- Meta webhook verification against the live HTTPS endpoint (depends on the
  above, and on wa-ingest-svc existing — Wave 1, issue #13, in progress by
  another agent concurrently with this one).
