---
name: issue-42-deploy-hardening
description: Issue #42 pre-first-deploy security hardening (S1/S3/S5/S6 + 3 nits from the PR #40 audit) — what was built, verified, and the reasoning behind each fix's shape
metadata:
  type: project
---

Issue #42 (2026-07-06), worked directly on `develop` (no branch/PR — team lead asked
for a working-tree-only handoff to the main session). Follow-up from the
security-code-reviewer's PR #40 audit ([[issue-12-vps-deploy]]); see
`.claude/agent-memory/security-code-reviewer/2026-07-03-pr40-vps-deploy.md` for the
original findings.

## S1 — JWT metadata over HTTPS
Root cause: prod compose sets `Jwt__Authority: http://core:8080` (an internal compose
hop) but every wa-*-svc's `Program.cs` sets `RequireHttpsMetadata = !IsDevelopment()`,
so in `ASPNETCORE_ENVIRONMENT=Production` the JWKS fetch demands HTTPS against a plain
`http://` Authority and every authenticated request 500s.

Fix shape (all 5 files: WaIntel/WaGateway/WaIngest/WaAdmin/WaBilling `.WebApi/Program.cs`):
`opts.RequireHttpsMetadata = builder.Configuration.GetValue<bool?>("Jwt:RequireHttpsMetadata")
?? !builder.Environment.IsDevelopment();` — i.e. an explicit override wins, otherwise
identical to the old behavior. **Deliberately not** a flat `default: true` — that would
have changed the effective default in every environment. The nullable-coalesce form means
zero behavior change unless the knob is set, which keeps the diff self-evidently mapped to
the finding for re-review. `core.WebApi` was NOT touched — it validates JWTs in-process
against its own signing key (no `Authority`/JWKS at all), confirmed by grep before touching
anything, so S1 doesn't apply there.

Compose side: added `Jwt__RequireHttpsMetadata: "false"` to each of the 5 wa-*-svc
environment blocks (not to `core`, not to `wavio-gateway` which doesn't validate JWTs),
each with an inline comment naming the trust boundary (Caddy terminates TLS at the edge;
this hop never leaves the compose-internal network).

**Considered and rejected**: factoring the identical ~10-line `AddJwtBearer` block into a
shared `wavio.Utilities` helper (the team lead's prompt explicitly raised this as the
reuse-first move). Decided against it for this task — it would touch the same 5 files
either way but adds a new shared-code surface for the security reviewer to re-verify,
working against the "keep each fix surgical and self-evidently mapped to its finding"
instruction. Flagged as a good candidate for a dedicated follow-up cleanup, not bundled here.

## S3 — deploy.yml ssh-action supply chain
Pinned `appleboy/ssh-action@v1` to the commit SHA `0ff4204d59e8e51228ff73bce53f80d53301dee2`.
Verified via `gh api repos/appleboy/ssh-action/tags` that this SHA is *both* what the
mutable `v1` tag and the immutable `v1.2.5` release tag point to (same commit object) —
don't just trust a tag name, cross-check the tags list. Added `fingerprint:` input wired to
a new `VPS_HOST_FINGERPRINT` secret, with the acquisition command lifted from the action's
own README (`ssh <host> ssh-keygen -l -f /etc/ssh/ssh_host_ed25519_key.pub | cut -d' ' -f2`)
plus a `ssh-keyscan` alternative for when the operator has no session yet.

## S5 — encrypt.sh demo-recipient guard
One-line `grep -q "$DEMO_RECIPIENT" .sops.yaml` check added before the usage/arg
validation, exits 1 with instructions if the throwaway demo age key
(`age1yuwpassvdgaxvqw9lvp2e3eqs7h9hemharsxayhkwsj3msw4pqds8dzz3s`) is still the active
recipient. Verified both directions live: tripped (exit 1, no sops invocation) against the
real repo `.sops.yaml`; then in a scratch copy with a freshly generated real age keypair
swapped in, the guard passed and a full encrypt→decrypt round trip succeeded.

## S6 — migrator superuser connection string
Moved from compose `command:` (world-readable via `/proc/<pid>/cmdline` and
`docker inspect` while the one-shot container runs) to an `environment:` entry
(`ConnectionStrings__Admin`). No C# change needed — `wavio.DbMigrator/Program.cs`'s
`ResolveConnectionString()` already checked this exact env var as its second priority
(after CLI args, before the dev-only default), a detail from the original issue #12 build
that made this a pure compose-file fix. Verified with
`docker compose -f docker-compose.prod.yml --profile tools config` (migrator is
`profiles: ["tools"]`, invisible to plain `config` without that flag) — confirmed
`command:` now only has `--migrations-dir` and the password lives solely in `environment:`.

## Nit — PII key over-injected into wavio-gateway
The shared `x-dotnet-service` anchor previously put `Pii__EncryptionKey` in every
service's environment via `<<: *dotnet-env`, including `wavio-gateway` (the YARP reverse
proxy), which never calls `AddSharedDataModel()`/touches `WavioDbContext` — confirmed by
grep across every `Program.cs`. Fix: pulled `Pii__EncryptionKey` out of the shared anchor
entirely and added it explicitly to each of the 6 services that do need it (`core` +
the 5 wa-*-svc). `wavio-gateway` now gets zero PII-related config. Note: `WaGateway.WebApi`
(compose service `wa-gateway-svc`, a real business API) and `wavio.Gateway` (compose
service `wavio-gateway`, the YARP proxy) are two different projects with confusingly
similar names — the nit was about the latter only.

## Nit — secret interpolation in deploy.yml shell conditional
The "Check VPS secrets are configured" step tested `${{ secrets.VPS_HOST }}` etc. directly
inside a `run:` bash `if [ -z "..." ]`. Restructured to pass secrets via `env:` and test
`$VPS_HOST` etc. as shell variables instead — keeps `${{ secrets.* }}` out of shell string
interpolation entirely.

## Nit — unpinned base images
Pinned by digest, all resolved locally (Colima was already running; no need to start it):
`mcr.microsoft.com/dotnet/sdk@sha256:ea8bde36c11b6e7eec2656d0e59101d4462f6bd630730f2c8201ed0572b295d5`,
`mcr.microsoft.com/dotnet/aspnet@sha256:7644f992230d35cf230017189d4038c0ae0f7388b13f4f7ae1900a155bafb597`
(Dockerfile ARGs), and in `docker-compose.prod.yml`: `postgres:16@sha256:fe03a76...`,
`rabbitmq:3-management@sha256:e582c0b...`, `caddy:2-alpine@sha256:5f5c864...`. Each pin has
an inline comment on how/when it was resolved (`docker pull` + `docker inspect --format=
'{{index .RepoDigests 0}}'`, 2026-07-06) so a future bump is a deliberate re-pin, not a
silent drift.

## Verification summary
`dotnet build wavio.slnx` — 0 errors, 153 pre-existing warnings (unchanged). `dotnet test
wavio.slnx` — 526/526 passed across 6 test projects (WaAdmin, WaBilling, WaGateway,
WaIngest, WaIntel, wavio.Utilities). `docker compose -f docker-compose.prod.yml config`
and `--profile tools config` both validate clean with dummy env vars.
`actionlint .github/workflows/deploy.yml` clean. `encrypt.sh` guard proven both ways (trip
+ pass) against real and scratch `.sops.yaml`.

## Definition of done
All four MUST items (S1/S3/S5/S6) and all three nits from issue #42 are closed. Nothing
was found already-resolved — all seven were live gaps at the time of this pass. No new
branch/PR was created per the team lead's explicit instruction; changes are sitting on
`develop` for the main session to review/commit, and a security-code-reviewer pass was
requested to re-verify.
