#!/usr/bin/env bash
# Restore drill (issue #12 acceptance criterion: "nightly dump restores to a
# scratch DB successfully"). Restores the most recent (or an explicitly given)
# dump into a throwaway database on the SAME running postgres container,
# verifies a handful of tables round-tripped with the expected row counts, then
# drops the scratch database. Never touches the real `waplatform` database.
#
# Usage:
#   ./deploy/db-backup/restore_drill.sh [path/to/dump.gz]
#   (defaults to the newest file in BACKUP_DIR matching waplatform_*.dump.gz)
#
# Env overrides: POSTGRES_CONTAINER, POSTGRES_DB, POSTGRES_USER, BACKUP_DIR,
# SCRATCH_DB.
#
# Intended to run periodically (e.g. weekly, alongside the nightly dump cron)
# so a broken backup is caught before it's ever actually needed.

set -euo pipefail

POSTGRES_CONTAINER="${POSTGRES_CONTAINER:-wavio-postgres}"
POSTGRES_DB="${POSTGRES_DB:-waplatform}"
POSTGRES_USER="${POSTGRES_USER:-postgres}"
BACKUP_DIR="${BACKUP_DIR:-/opt/wavio/backups}"
SCRATCH_DB="${SCRATCH_DB:-waplatform_restore_drill}"

# This script unconditionally DROPs SCRATCH_DB (both up front and in the
# cleanup trap on exit) — if misconfigured to equal POSTGRES_DB, it destroys
# the real database. Refuse to proceed rather than let that happen.
if [ "$SCRATCH_DB" = "$POSTGRES_DB" ]; then
    echo "[restore_drill] REFUSING to run: SCRATCH_DB ('$SCRATCH_DB') must not equal POSTGRES_DB ('$POSTGRES_DB') — this script drops SCRATCH_DB unconditionally, on every run and on exit. Set SCRATCH_DB to a different name." >&2
    exit 1
fi

DUMP_FILE="${1:-}"
if [ -z "$DUMP_FILE" ]; then
    DUMP_FILE="$(find "$BACKUP_DIR" -maxdepth 1 -name 'waplatform_*.dump.gz' -print0 \
        | xargs -0 ls -t 2>/dev/null | head -n1 || true)"
fi

if [ -z "$DUMP_FILE" ] || [ ! -f "$DUMP_FILE" ]; then
    echo "[restore_drill] No dump file found (looked in ${BACKUP_DIR}, or pass a path explicitly)." >&2
    exit 1
fi

echo "[restore_drill] $(date -u -Iseconds) — restoring ${DUMP_FILE} into scratch DB '${SCRATCH_DB}'"

cleanup() {
    docker exec "$POSTGRES_CONTAINER" \
        psql -v ON_ERROR_STOP=1 --username "$POSTGRES_USER" --dbname postgres \
        -c "DROP DATABASE IF EXISTS ${SCRATCH_DB} WITH (FORCE);" > /dev/null
}
trap cleanup EXIT

docker exec "$POSTGRES_CONTAINER" \
    psql -v ON_ERROR_STOP=1 --username "$POSTGRES_USER" --dbname postgres \
    -c "DROP DATABASE IF EXISTS ${SCRATCH_DB} WITH (FORCE);" > /dev/null
docker exec "$POSTGRES_CONTAINER" \
    psql -v ON_ERROR_STOP=1 --username "$POSTGRES_USER" --dbname postgres \
    -c "CREATE DATABASE ${SCRATCH_DB};" > /dev/null

gunzip -c "$DUMP_FILE" | docker exec -i "$POSTGRES_CONTAINER" \
    pg_restore --username "$POSTGRES_USER" --dbname "$SCRATCH_DB" \
    --no-owner --no-privileges

echo "[restore_drill] restore complete — verifying row counts against ${POSTGRES_DB}"

# Compares a handful of representative tables (one per migrated schema) between
# the restored scratch DB and the live source DB. Any mismatch fails the drill.
FAILURES=0
for table in tenancy.tenants identity_access.permissions identity_access.roles \
             system.feature_flags kernel.outbox_events public.schema_migrations; do
    src_count="$(docker exec "$POSTGRES_CONTAINER" \
        psql -v ON_ERROR_STOP=1 -qAtX --username "$POSTGRES_USER" --dbname "$POSTGRES_DB" \
        -c "SELECT count(*) FROM ${table};")"
    restored_count="$(docker exec "$POSTGRES_CONTAINER" \
        psql -v ON_ERROR_STOP=1 -qAtX --username "$POSTGRES_USER" --dbname "$SCRATCH_DB" \
        -c "SELECT count(*) FROM ${table};")"

    if [ "$src_count" = "$restored_count" ]; then
        echo "  ok   - ${table}: ${restored_count} rows (matches source)"
    else
        echo "  FAIL - ${table}: source=${src_count} restored=${restored_count}"
        FAILURES=$((FAILURES + 1))
    fi
done

if [ "$FAILURES" -gt 0 ]; then
    echo "[restore_drill] FAILED — ${FAILURES} table(s) did not match. Dump ${DUMP_FILE} may be broken." >&2
    exit 1
fi

echo "[restore_drill] all checks passed — ${DUMP_FILE} is a valid, restorable backup."
