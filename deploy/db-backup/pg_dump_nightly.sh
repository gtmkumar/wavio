#!/usr/bin/env bash
# Nightly pg_dump + rotation (issue #12). v1 backup strategy per docs/BUILD_PLAN.md
# ("v1: nightly pg_dump cron with rotation; PITR is a Wave 2+ hardening task").
#
# Intended to run on the VPS host via crontab, e.g.:
#   0 2 * * * /opt/wavio/deploy/db-backup/pg_dump_nightly.sh >> /var/log/wavio-backup.log 2>&1
#
# Runs `pg_dump` through `docker exec` against the running postgres container
# (no need for a pg_dump binary on the host — only Docker). Writes a
# custom-format (-Fc) dump, which pg_restore/psql can both replay, gzipped, into
# BACKUP_DIR, then deletes dumps older than RETAIN_DAYS.
#
# Env overrides: POSTGRES_CONTAINER, POSTGRES_DB, POSTGRES_USER, BACKUP_DIR,
# RETAIN_DAYS.

set -euo pipefail

POSTGRES_CONTAINER="${POSTGRES_CONTAINER:-wavio-postgres}"
POSTGRES_DB="${POSTGRES_DB:-waplatform}"
POSTGRES_USER="${POSTGRES_USER:-postgres}"
BACKUP_DIR="${BACKUP_DIR:-/opt/wavio/backups}"
RETAIN_DAYS="${RETAIN_DAYS:-14}"

TIMESTAMP="$(date -u +%Y%m%dT%H%M%SZ)"
DUMP_FILE="${BACKUP_DIR}/waplatform_${TIMESTAMP}.dump.gz"

mkdir -p "$BACKUP_DIR"

echo "[pg_dump_nightly] $(date -u -Iseconds) — dumping ${POSTGRES_DB} from ${POSTGRES_CONTAINER} -> ${DUMP_FILE}"

docker exec "$POSTGRES_CONTAINER" \
    pg_dump --username "$POSTGRES_USER" --dbname "$POSTGRES_DB" --format=custom \
    | gzip > "$DUMP_FILE"

# Sanity check: a valid custom-format dump must not be empty/truncated.
if [ ! -s "$DUMP_FILE" ]; then
    echo "[pg_dump_nightly] FAILED — ${DUMP_FILE} is empty, removing and aborting." >&2
    rm -f "$DUMP_FILE"
    exit 1
fi

echo "[pg_dump_nightly] wrote $(du -h "$DUMP_FILE" | cut -f1) — rotating dumps older than ${RETAIN_DAYS} days"

find "$BACKUP_DIR" -maxdepth 1 -name 'waplatform_*.dump.gz' -mtime "+${RETAIN_DAYS}" -print -delete

echo "[pg_dump_nightly] done."
