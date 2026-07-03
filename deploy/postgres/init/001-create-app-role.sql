-- Wavio dev bootstrap (issue #9)
--
-- Runs once, on first container init, against the `waplatform` database (created
-- automatically by the official postgres image from POSTGRES_DB — see
-- docker-compose.dev.yml). Creates the non-superuser application role that every
-- service's ConnectionStrings:Default points at (wavio.AppHost/AppHost.cs).
--
-- RLS policies (spec §5, app.tenant_id GUC) are enforced against this role — it must
-- NEVER be superuser or BYPASSRLS, or tenant isolation silently stops applying.
--
-- Schema-level grants (tenancy_org, identity_access, kernel, ...) are applied by the
-- V001+ migrations themselves as each schema is created — see issue #10.

DO $$
BEGIN
    IF NOT EXISTS (SELECT FROM pg_catalog.pg_roles WHERE rolname = 'app_user') THEN
        CREATE ROLE app_user LOGIN PASSWORD 'app_user'
            NOSUPERUSER NOCREATEDB NOCREATEROLE NOBYPASSRLS;
    END IF;
END
$$;

GRANT CONNECT ON DATABASE waplatform TO app_user;
GRANT USAGE ON SCHEMA public TO app_user;
