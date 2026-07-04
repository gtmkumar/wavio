#!/usr/bin/env bash
# Production Postgres bootstrap (issue #12).
#
# db/migrations/V001__tenancy.sql creates app_user itself, but ONLY if it
# doesn't already exist — and only with the well-known dev password
# ('app_user'/'app_user'). Its own header says so explicitly: "production
# creates app_user with a real password before running migrations — this
# bootstrap only fires on dev/CI." This script IS that production bootstrap:
# it runs once, on first container init (docker-entrypoint-initdb.d executes
# it before anything else, including before wavio.DbMigrator is ever run), and
# creates app_user with the real password from the SOPS/age-decrypted prod env
# file — so by the time V001 runs, its `IF NOT EXISTS` check finds the role
# already present and skips creating the weak dev credential.
#
# POSTGRES_APP_PASSWORD is supplied via docker-compose.prod.yml's `environment:`
# on the postgres service, sourced from the decrypted prod .env (never
# committed — see deploy/secrets/README.md).

set -euo pipefail

: "${POSTGRES_APP_PASSWORD:?POSTGRES_APP_PASSWORD must be set — see deploy/secrets/README.md}"

psql -v ON_ERROR_STOP=1 --username "$POSTGRES_USER" --dbname "$POSTGRES_DB" <<-EOSQL
    DO \$\$
    BEGIN
        IF NOT EXISTS (SELECT FROM pg_catalog.pg_roles WHERE rolname = 'app_user') THEN
            CREATE ROLE app_user LOGIN PASSWORD '$POSTGRES_APP_PASSWORD'
                NOSUPERUSER NOCREATEDB NOCREATEROLE NOBYPASSRLS;
        END IF;
    END
    \$\$;

    GRANT CONNECT ON DATABASE $POSTGRES_DB TO app_user;
    GRANT USAGE ON SCHEMA public TO app_user;
EOSQL
