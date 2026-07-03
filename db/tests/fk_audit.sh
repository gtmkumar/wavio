#!/usr/bin/env bash
# FK-audit gate (issue #11): every uuid *_id column must have a FK constraint,
# unless its "schema.table.column" is explicitly allowlisted in
# db/tests/fk_audit_allowlist.txt (each entry cross-referenced in db/README.md
# "FK audit rules"). Lesson learned: a prior (Laundry Ghar) schema audit found 147
# missing FKs — this gate exists so that never recurs silently.
#
# Partition children (relispartition = true) are excluded from the scan — they
# inherit the parent's FK constraints (db/README.md: "audit parents only").
#
# Run after migrations are applied. Usage:
#   ./db/tests/fk_audit.sh
# Override connection via ADMIN_URL env var if needed.

set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
ALLOWLIST_FILE="${SCRIPT_DIR}/fk_audit_allowlist.txt"

PGHOST="${PGHOST:-localhost}"
PGPORT="${PGPORT:-5432}"
PGDB="${PGDATABASE:-waplatform}"
ADMIN_URL="${ADMIN_URL:-postgresql://postgres:postgres@${PGHOST}:${PGPORT}/${PGDB}}"

psql_q() { psql "$ADMIN_URL" -v ON_ERROR_STOP=1 -qAtX -c "$1"; }

# Runs a query and populates the named array with one element per output row.
# NOT `mapfile -t arr < <(psql_q ...)`: process substitution's exit status is
# invisible to `set -e`/pipefail, so a failed connection (e.g. bad password)
# would otherwise be swallowed — mapfile happily "reads" zero lines from the
# broken pipe and the gate reports "0 columns checked" and exits 0. Capturing
# into a variable first makes the failure visible to `||`.
psql_lines() {
    local -n _out_array="$1"
    local raw
    raw="$(psql_q "$2")" || {
        echo "[fk_audit] psql query failed — aborting (see error above)." >&2
        exit 1
    }
    _out_array=()
    # Guard the empty-result edge case: `mapfile <<< ""` yields one phantom
    # empty-string element instead of a zero-length array.
    if [[ -n "$raw" ]]; then
        mapfile -t _out_array <<< "$raw"
    fi
}

echo "== FK audit: every uuid *_id column must have a FK, or be allowlisted =="

psql_lines all_id_columns "
    SELECT n.nspname || '.' || c.relname || '.' || a.attname
    FROM pg_attribute a
    JOIN pg_class c ON c.oid = a.attrelid
    JOIN pg_namespace n ON n.oid = c.relnamespace
    JOIN pg_type t ON t.oid = a.atttypid
    WHERE a.attname LIKE '%\_id' ESCAPE '\'
      AND t.typname = 'uuid'
      AND a.attnum > 0
      AND NOT a.attisdropped
      AND c.relkind IN ('r', 'p')
      AND NOT c.relispartition
      AND n.nspname NOT IN ('pg_catalog', 'information_schema')
    ORDER BY 1;
"

psql_lines fk_columns "
    SELECT DISTINCT n.nspname || '.' || c.relname || '.' || a.attname
    FROM pg_constraint co
    JOIN pg_class c ON c.oid = co.conrelid
    JOIN pg_namespace n ON n.oid = c.relnamespace
    JOIN unnest(co.conkey) AS ck(attnum) ON true
    JOIN pg_attribute a ON a.attrelid = c.oid AND a.attnum = ck.attnum
    WHERE co.contype = 'f'
    ORDER BY 1;
"

mapfile -t allowlist < <(grep -v '^[[:space:]]*#' "$ALLOWLIST_FILE" | grep -v '^[[:space:]]*$' || true)

declare -A has_fk=()
for col in "${fk_columns[@]}"; do has_fk["$col"]=1; done

declare -A allowed=()
for col in "${allowlist[@]}"; do allowed["$col"]=1; done

violations=()
for col in "${all_id_columns[@]}"; do
    if [[ -z "${has_fk[$col]:-}" && -z "${allowed[$col]:-}" ]]; then
        violations+=("$col")
    fi
done

echo "Checked ${#all_id_columns[@]} uuid *_id column(s); ${#allowlist[@]} allowlisted."

if [ "${#violations[@]}" -gt 0 ]; then
    echo "FK AUDIT FAILED — the following uuid *_id column(s) have no FK and are not allowlisted:"
    for v in "${violations[@]}"; do echo "  - $v"; done
    echo ""
    echo "Add a FK constraint, or if the exclusion is genuinely deliberate (polymorphic"
    echo "reference, trace id, cross-service id, ...), add the entry to"
    echo "db/tests/fk_audit_allowlist.txt with a reason, and cross-reference it in"
    echo "db/README.md \"FK audit rules\"."
    exit 1
fi

echo "== FK audit: all uuid *_id columns have a FK or a documented allowlist entry =="
