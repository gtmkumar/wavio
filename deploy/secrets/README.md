# Wavio production secrets (issue #12)

SOPS + age — both free/OSS, no cloud KMS (docs/BUILD_PLAN.md "Secrets (no cloud
KMS)"). Encrypted secrets live in git as `*.enc` files; only someone holding the
real age private key can decrypt them.

## One-time setup (do this before anything else — the committed `.sops.yaml`
## ships with a throwaway demo recipient, not a usable one)

```bash
# Generate your own age keypair. Keep the private key OFFLINE — a password
# manager or an encrypted USB, never in git, never on the VPS's disk unencrypted
# for longer than it takes to decrypt.
age-keygen -o ~/.config/sops/age/keys.txt
# prints: Public key: age1...
```

Replace the placeholder recipient in `.sops.yaml` (repo root) with that public
key, for both the `*.env` and `*.pem` rules. The public key is, well, public —
safe to commit. Anyone who needs to encrypt/decrypt prod secrets needs the
matching **private** key out-of-band (never via git).

## Encrypting secrets (whoever holds the private key, on their own machine)

```bash
cp deploy/secrets/prod.env.example deploy/secrets/prod.env
# ... fill in real values ...
export SOPS_AGE_KEY_FILE=~/.config/sops/age/keys.txt
./deploy/secrets/encrypt.sh deploy/secrets/prod.env
# -> writes deploy/secrets/prod.env.enc — commit THIS file, never prod.env itself

# RS256 signing key (core only) ships as its own encrypted file — multi-line
# PEM text doesn't survive KEY=VALUE .env parsing reliably:
openssl genrsa -out deploy/secrets/jwt-private-key.pem 2048
./deploy/secrets/encrypt.sh deploy/secrets/jwt-private-key.pem
# -> writes deploy/secrets/jwt-private-key.pem.enc — commit this, not the .pem

# Clean up the plaintext locally once both .enc files exist (they're gitignored
# — deploy/secrets/*.env and deploy/secrets/*.pem — but delete them anyway):
rm deploy/secrets/prod.env deploy/secrets/jwt-private-key.pem
```

To change a secret later: repeat the same commands (decrypt if you need the old
value first, edit, re-encrypt, commit the new `.enc`).

## The committed `prod.env.enc` / `jwt-private-key.pem.enc` are demo files — not real secrets

`deploy/secrets/prod.env.enc` and `deploy/secrets/jwt-private-key.pem.enc` **are
committed to this repo**, encrypted under the same throwaway demo age
recipient named in `.sops.yaml`. This exists to prove the SOPS/age pipeline
actually works end to end from a fresh clone — not to ship anything usable:

- Every value inside `prod.env.enc` is a literal placeholder string like
  `CHANGE_ME_FAKE_DEMO_VALUE_DO_NOT_USE_IN_PRODUCTION` — there is nothing
  secret to leak even if someone did decrypt it.
- `jwt-private-key.pem.enc` wraps a throwaway RSA key generated solely for
  this demo, never used to sign anything, associated with no real deployment.
- The demo age keypair's **private** half was used once, locally, to produce
  these two files (and to verify they decrypt back byte-identical to the
  plaintext that was encrypted) — then deleted. It is not stored anywhere,
  not in this repo, not in agent memory, not on any machine after that
  verification. Nobody, including the author of this PR, can decrypt these
  two files anymore.
- **This means the committed demo recipient in `.sops.yaml` is now write-only
  from everyone's perspective** — exactly the state you want before real
  secrets ever get encrypted with it. Per the "One-time setup" section above:
  generate your **own** age keypair and replace `.sops.yaml`'s recipient
  before encrypting anything real. Never reuse the demo recipient for actual
  production values, even though its public half is sitting in git — a
  public key being public doesn't make it *yours*.

## Decrypting on the VPS (deploy time)

The VPS needs the private key too (out-of-band — scp it once over SSH, or paste
it interactively; never put it in the git repo or in an image):

```bash
export SOPS_AGE_KEY_FILE=/root/.config/sops/age/keys.txt   # 0600, root-only

./deploy/secrets/decrypt.sh deploy/secrets/prod.env.enc /opt/wavio/.env
./deploy/secrets/decrypt.sh deploy/secrets/jwt-private-key.pem.enc \
    /opt/wavio/secrets/jwt-private-key.pem
```

Both outputs are written `chmod 600`. `docker-compose.prod.yml` reads
`/opt/wavio/.env` via `env_file:` and bind-mounts
`/opt/wavio/secrets/jwt-private-key.pem` read-only into the `core` container at
`/run/secrets/jwt-private-key.pem` (path referenced by `JWT_PRIVATE_KEY_PATH` in
the env file, which the app reads as `Jwt__PrivateKeyPath`).

## What's in `prod.env` (see `prod.env.example` for the authoritative list)

| Key | Used by | Notes |
|---|---|---|
| `POSTGRES_PASSWORD` | migrations only (`wavio.DbMigrator --connection-string`) | superuser; no running service uses it |
| `POSTGRES_APP_PASSWORD` | `deploy/postgres/init-prod/001-create-app-role.sh` | must be set before first `docker compose up` — see that script |
| `RABBITMQ_DEFAULT_USER` / `_PASS` | rabbitmq container | never published to the host — internal network only |
| `PII_ENCRYPTION_KEY` | every service touching `WavioDbContext` | 32 random bytes, base64 (`openssl rand -base64 32`); rotating breaks existing `enc:v1` values until re-saved |
| `JWT_PRIVATE_KEY_PATH` | `core` only | points at the decrypted PEM (see above), not the key itself |
| `WAVIO_DOMAIN` | Caddy | must resolve to the VPS's public IP for ACME HTTP-01 to succeed |
| `GHCR_OWNER` / `IMAGE_TAG` | `docker compose pull` | which images to pull from GHCR |

## Verifying this tooling actually works (what was tested before this shipped)

A full round-trip was run locally with a throwaway age keypair (generated,
used, and discarded — never committed): encrypted a realistic `prod.env` +
a real 2048-bit RSA PEM, decrypted both back, and diffed the result against
the originals (byte-identical for both). Also confirmed decryption fails
loudly (nonzero exit, no output file left behind) when the right key isn't
available — see `.claude/agent-memory/dotnet-backend-developer/issue-12-vps-deploy.md`
for the exact commands run.

## Out of scope here (user-side, needs a real VPS/domain)

- Actually generating the *real* production age keypair and getting the
  private half onto the VPS — that's an operational step for whoever runs
  this, not something committable.
- A real Let's Encrypt cert issuance (needs `WAVIO_DOMAIN` to genuinely resolve
  to the VPS's public IP) — see the top-level deploy README for what was
  verified instead (Caddy config validation + a local TLS smoke test).
