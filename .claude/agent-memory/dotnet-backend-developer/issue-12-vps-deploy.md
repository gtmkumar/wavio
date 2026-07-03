---
name: issue-12-vps-deploy
description: What was built for issue #12 (VPS deployment baseline), real bugs found during verification, and what's genuinely user-side
metadata:
  type: project
---

Issue #12 (2026-07-03), branch `feature/12-vps-deploy`. Originally based on
`feature/10-db-migrations`; **re-based onto `feature/11-ci-pipeline`** (via a
non-force merge commit, not a rebase — see "Retargeting the PR" below) once
#11 was QA-approved and the orchestrator asked for the stack to continue on
top of it. PR #40, base retargeted with `gh pr edit --base`.

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

## Round 3 (same day): security-code-reviewer's four cheap fixes

Verdict was APPROVE with four cheap items required before merge (the rest
went to issue #42, pre-first-deploy hardening — not this PR):

- **S7 (the important one) — AAD.** `AesGcmEnvelopeCipher` had no additional
  authenticated data, so a ciphertext wasn't bound to the row/tenant it
  belongs to: two encrypted WABA tokens could be swapped between rows (bad
  `UPDATE`, or malicious DB access) and each would still decrypt
  "successfully" into the wrong row's plaintext — every envelope is otherwise
  self-consistent (its own random data key), so nothing about the ciphertext
  itself reveals it's misplaced. Added a **required** (non-optional, so
  callers can't silently forget it) `byte[]? aad` parameter to
  `Encrypt`/`Decrypt`/`Rewrap`, folded into `AesGcm`'s `associatedData` for
  both the key-wrap and data layers. New tests: matching AAD round-trips,
  wrong/missing AAD throws `AuthenticationTagMismatchException`, and one test
  that plays out the exact swap scenario end-to-end (encrypt two "rows" with
  their own AAD, swap the ciphertext between them, prove decrypting with the
  *receiving* row's AAD fails while the *originating* row's AAD still works —
  i.e. the swap is detected, not just "some AAD mismatch throws"). 19/19 green
  (14 existing + 5 new).
- **S4a — dump permissions.** `pg_dump_nightly.sh` now sets `umask 077`
  before creating `BACKUP_DIR`/the dump file. Verified: a fresh
  script-created directory is `700`, the dump file `600` — previously both
  inherited the ambient (typically `022`) umask, so `644`/`755`, i.e.
  world-readable dumps of the entire database.
- **S4b — restore-drill guard.** `restore_drill.sh` unconditionally `DROP`s
  `SCRATCH_DB` (both up front and in the `EXIT` trap) — if `SCRATCH_DB` were
  ever misconfigured to equal `POSTGRES_DB`, it would destroy the real
  database. Added a same-value check as the very first thing after resolving
  the env vars, before any `docker exec` call. Verified both directions:
  `SCRATCH_DB=waplatform` (matching the real DB name) refuses immediately
  with a clear message and touches no Docker command at all; the normal path
  (`SCRATCH_DB` unset, default `waplatform_restore_drill`) still dumps and
  restores successfully.
- **S2 — ufw comment was making a false claim.** The script's comment implied
  `ufw deny 5432/tcp` etc. would block a future accidentally-published Docker
  port. That's false: Docker inserts its own ACCEPT rules directly into the
  `DOCKER`/`FORWARD` iptables chains, ahead of ufw's `INPUT`-chain rules, so a
  published container port stays reachable from the internet regardless of
  what ufw says — a well-known Docker+ufw interaction, not a ufw bug.
  Corrected the comment and documented the real mitigations inline: never
  publish those ports in compose (current, correct state — but ufw can't
  catch a regression here, so said so explicitly), bind to `127.0.0.1` if a
  host-only port is ever needed, or add rules to the `DOCKER-USER` chain
  (the one chain Docker never overwrites) for a firewall-layer guarantee.
  Re-ran the script in the same privileged-container harness as before to
  confirm the corrected comment text shows up in the live `ufw status
  verbose` output, not just in the source file.

All four re-verified for real (not just re-read) after fixing: full
`dotnet build`/`dotnet test` clean (135 pre-existing warnings unchanged, 19/19
tests), both backup scripts re-run against the live dev Postgres, ufw script
re-run in a privileged container. Pushed as a follow-up commit on
`feature/12-vps-deploy`; CI green on the updated PR.

## Genuinely user-side (documented, not faked)
- Real age keypair generation + getting the private half onto the VPS.
- Real Let's Encrypt cert issuance (needs `WAVIO_DOMAIN` → real public IP).
- `VPS_HOST`/`VPS_USER`/`VPS_SSH_KEY` GitHub secrets for the `ssh-deploy` job.
- Meta webhook verification against the live HTTPS endpoint (depends on the
  above, and on wa-ingest-svc existing — Wave 1, issue #13, in progress by
  another agent concurrently with this one).

## Round 2 (same day): retargeting onto #11, envelope encryption, and a real Caddy bug

The orchestrator asked to continue the branch stack onto `feature/11-ci-pipeline`
(now QA-approved) and add scope I hadn't done initially. Rather than reject/redo,
retargeted the existing PR:

### Retargeting the PR (git mechanics worth remembering)
`git rebase origin/feature/11-ci-pipeline` on the shared checkout was correctly
**blocked by the auto-mode permission classifier** when I tried to force-push
the result — force-push rewrites remote history and only a teammate (not the
user) had asked for it. Used a plain **merge** instead
(`git merge origin/feature/11-ci-pipeline --no-edit`, one conflict in
`MEMORY.md`, resolved by keeping both sides' index entries) — a merge commit
is purely additive, so a normal (non-force) `git push` sufficed. Lesson: when
you need a branch to sit on a new base and can't/shouldn't force-push, merge
the new base in rather than rebasing — same end state (branch now contains
the new base's history), no permission fight, no history rewrite.

### A second, worse shared-working-directory incident — moved to a real git worktree
Mid-recovery, the shared checkout's branch changed *again* while I was mid-`git
stash` (another teammate's concurrent checkout), and `git checkout` back to my
own branch dragged the other agent's **staged** index content along with it —
worse than before, since staged content risks being swept into a commit.
Fixed properly this time instead of repeating the same fragile dance: created
an actual isolated `git worktree` (`git worktree add --detach <path> <sha>`,
since the branch was already checked out in the shared dir so a normal
worktree checkout was refused) at `/Users/gtmkumar/wavio-worktrees/vps-deploy`
and did all remaining work there, immune to any other agent's concurrent
checkouts. This is the right tool for this team setup — recommend it (or
`EnterWorktree`) proactively next time rather than after two near-misses.

### New scope added
- `wavio.Utilities/Crypto/{IEnvelopeCipher,AesGcmEnvelopeCipher}.cs` — envelope
  encryption (random per-value data key, wrapped by a master key) for Meta
  system-user tokens, replacing the spec's KMS references. Minimal surface
  (Encrypt/Decrypt/Rewrap), no consumers yet (Wave 1 wires it up).
- `wavio.Utilities.Tests` — **first real test project in the repo** (xUnit).
  14 tests. Running them for real caught a genuine assumption bug: .NET's
  `AesGcm.Decrypt` throws `AuthenticationTagMismatchException` (a
  `CryptographicException` subtype) on tag-mismatch, not the base
  `CryptographicException` — and xUnit's `Assert.Throws<T>` requires an exact
  type match, not "is-a". Had to retarget three assertions to the specific
  subtype. `CA1707` (no underscores in identifiers) suppressed project-wide in
  the test csproj only — it fights xUnit's standard readable test-naming
  convention.
- `.github/workflows/deploy.yml` — `ssh-deploy` job now has an explicit
  "Check VPS secrets are configured" step that emits a `::notice::` and skips
  the actual SSH step cleanly (not a failure) when `VPS_HOST`/`VPS_USER`/
  `VPS_SSH_KEY` aren't set, instead of letting `appleboy/ssh-action` fail on a
  connection error. Linted clean with `actionlint` (installed via brew).
- `deploy/secrets/prod.env.enc` + `jwt-private-key.pem.enc` — **committed**,
  real working encrypted files (every value a literal `CHANGE_ME_FAKE_DEMO_...`
  placeholder), proving the SOPS/age pipeline works end to end from a fresh
  clone. Encrypted with the same throwaway demo age recipient already in
  `.sops.yaml`; used its private half once to produce + verify these files,
  then deleted it — nobody, including me, can decrypt them anymore. That's
  the correct state for a demo recipient nobody should reuse for real secrets
  (documented explicitly in `deploy/secrets/README.md`).

### Real bug #4: Caddy's active health check marked the correctly-404 upstream DOWN
Brought the **full** `docker-compose.prod.yml` stack up locally, including
Caddy (with `tls internal` standing in for Let's Encrypt — no real domain
available), and actually curled `https://localhost/` end to end — this is
what the orchestrator explicitly asked verification to cover, and it caught
something my earlier standalone-Caddy-container smoke test (issue #12 round
1) did not, because that test used a simpler Caddyfile without the
`health_uri` directive the real committed one has.

Every request came back `503 no upstreams available`. Caddy's `logger
http.handlers.reverse_proxy.health_checker.active` reported `"status code out
of tolerances","status_code":404` — the active health checker requires a 2xx
response by default, but `wavio.Gateway` (and everything behind it)
legitimately 404s on unmatched routes in this Wave 0 state (exactly the
reasoning already documented in `docker-compose.prod.yml`'s own healthcheck
comment). Caddy marked the perfectly healthy upstream as down and refused to
proxy anything at all — **this would have made every real deployment
non-functional**, and `caddy validate` alone would never have caught it (it
only checks config syntax, not runtime behavior against a real upstream).

Fixed by removing the `health_uri`/`health_interval`/`health_timeout` block
entirely from `deploy/caddy/Caddyfile` — Docker Compose's own healthchecks +
`depends_on: condition: service_healthy` already gate startup ordering, so
Caddy re-implementing that against a route that legitimately 404s was pure
downside. Re-verified after the fix: real `server: Kestrel` / `via: 1.1
Caddy` headers, zero 503s across 5 repeated requests, multiple paths tested
(`/`, `/webhooks`, `/api/v1/messages`).

**Takeaway:** "bring the whole stack up and hit it for real" caught a bug
that config validation, unit-level Caddyfile review, and even an earlier
*simpler* smoke test all missed. When a verification step is explicitly
requested (as this one was), do the actual thing requested, not an
approximation of it — the gap between "validated" and "actually worked" was
exactly where this bug lived.
