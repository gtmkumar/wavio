---
name: fk-audit-gate-notes
description: How db/tests/fk_audit.sh works, a set -e/process-substitution gap that was found and fixed (commit 07e0d01), and the method used to cross-check the allowlist against db/README.md
metadata:
  type: project
---

**Status: FIXED (commit `07e0d01`, 2026-07-03, same day as found).** The gap
described below is historical — kept for the "why" in case a similar bug
pattern shows up elsewhere. Current `fk_audit.sh` uses a `psql_lines()`
helper (command substitution + explicit `||` exit check + empty-result
guard) instead of `mapfile < <(...)`. Re-verified: bad password now exits 1
with `[fk_audit] psql query failed — aborting (see error above).`; good path
unchanged (53/15/0). See [[review-pr38-ci-pipeline]] "Update" section.

`db/tests/fk_audit.sh` (added in PR #38 / issue #11): queries `pg_catalog`
directly for every `uuid` column named `*_id` (excluding partition children via
`NOT c.relispartition`), fails if a column has no FK constraint and isn't in
`db/tests/fk_audit_allowlist.txt` (schema.table.column, one per line, matched
1:1 against db/README.md's "FK audit rules" section).

## Confirmed-working (2026-07-03, PR #38 review)
- Positive case: against a freshly-migrated bare postgres:16 (V001-V006),
  script reports "Checked 53 uuid *_id column(s); 15 allowlisted" and exits 0.
- Negative case: removing one real allowlist entry
  (`kernel.outbox_events.aggregate_id`) makes the gate fail and name exactly
  that column, exit 1. Restored the file after, byte-identical (md5 verified).
- Allowlist completeness check: pulled the actual set of uuid `*_id` columns
  from `pg_catalog` and diffed against the allowlist file — every allowlist
  entry corresponds to a real existing column (no dead/stale entries), and
  since the gate reports 0 violations, no real column is missing from the
  list either. 1:1 against db/README.md's documented exclusion categories
  (polymorphic refs, trace/correlation ids, cross-service outbox id) — no
  extras, nothing missing.

## Real defect found: silent-pass on query failure

`fk_audit.sh` populates its arrays via `mapfile -t arr < <(psql_q "...")` —
**process substitution**, not a pipe. `set -euo pipefail` does NOT catch a
failing command inside `< <(...)`; only the exit status of `mapfile` itself
(reading from the fd) is checked, and `mapfile` succeeds even if it reads
nothing. Reproduced: pointed `ADMIN_URL` at a wrong password —

```
psql: error: ... FATAL: password authentication failed for user "postgres"
Checked 0 uuid *_id column(s); 15 allowlisted.
== FK audit: all uuid *_id columns have a FK or a documented allowlist entry ==
EXIT=0
```

The gate prints the psql error to stderr but **exits 0 and claims success**
with "Checked 0 columns". This is the same class of defect the FK-audit gate
exists to prevent (issue #11's own justification: 147 missing FKs slipped
through silently in a prior audit) — a quality gate whose safety net has an
identical silent-failure shape.

Currently low-risk in practice because the CI job's `ADMIN_URL` is derived
from the same hardcoded `postgres:postgres@localhost:5432` used by the
migration step immediately before it in the same job — if the connection
were broken, the migration step would already have failed the job first.
But it's a latent trap for later waves (credential rotation, a typo in the
`pg_catalog` SQL after a Postgres version bump, etc.) since the failure mode
is a green gate with a "Checked 0" line nobody reads.

**Suggested fix** (small, not gold-plating): capture the query output via
command substitution with an explicit exit-code check instead of process
substitution, e.g.:
```bash
raw="$(psql_q "...")" || { echo "FK audit: query failed" >&2; exit 1; }
mapfile -t all_id_columns <<< "$raw"
```
or add a `(( ${#all_id_columns[@]} > 0 ))` sanity assertion before trusting a
zero-violation result. Same gap exists for the `fk_columns` query, though
that direction fails safe (empty `fk_columns` → more false-positive
violations, not a false pass).

See [[review-pr38-ci-pipeline]] for the full review this was found in.
