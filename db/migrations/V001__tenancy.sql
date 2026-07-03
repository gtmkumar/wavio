-- V001__tenancy.sql
-- Schema: tenancy — tenants, tenant_settings, api_keys, external_tenant_refs.
-- Also bootstraps the cross-cutting pieces every later migration relies on:
--   * database roles (app_user, platform_admin) — idempotent, dev/CI only creates
--   * citext extension (identity_access.users.email, V005)
--   * public.schema_migrations version-tracking table
--   * app schema: RLS helper functions + generic month-partition maintenance
--
-- RLS pattern (spec §5): every tenant-scoped table gets ENABLE + FORCE ROW LEVEL
-- SECURITY and a policy comparing tenant_id (or id, for tenants itself) against
-- app.current_tenant_id(). The helper reads BOTH GUC spellings:
--   app.tenant_id          (spec §5 canonical name)
--   app.current_tenant_id  (name set by wavio.SharedDataModel RlsConnectionInterceptor)
-- The app.bypass_rls GUC set by the interceptor is deliberately IGNORED: any client
-- can set a GUC, so it is not a security boundary. Cross-tenant access requires
-- membership of the platform_admin role (or the superuser admin connection used for
-- dev seeding); every such grant is a deliberate, auditable DDL act, and all admin
-- mutations must be recorded in system.audit_log (app responsibility, spec §5).

------------------------------------------------------------------------------
-- Roles (cluster-wide, so guarded; production creates app_user with a real
-- password before running migrations — this bootstrap only fires on dev/CI).
------------------------------------------------------------------------------
DO $$
BEGIN
    IF NOT EXISTS (SELECT FROM pg_catalog.pg_roles WHERE rolname = 'app_user') THEN
        CREATE ROLE app_user LOGIN PASSWORD 'app_user'
            NOSUPERUSER NOCREATEDB NOCREATEROLE NOBYPASSRLS;
    END IF;
    IF NOT EXISTS (SELECT FROM pg_catalog.pg_roles WHERE rolname = 'platform_admin') THEN
        -- Membership role, never logs in directly. NOBYPASSRLS: bypass is granted
        -- by the policies below (policy-based, works with FORCE RLS), not by role
        -- attribute, so it applies uniformly and is visible in the DDL.
        CREATE ROLE platform_admin NOLOGIN
            NOSUPERUSER NOCREATEDB NOCREATEROLE NOBYPASSRLS;
    END IF;
END
$$;

GRANT CONNECT ON DATABASE waplatform TO app_user;
GRANT USAGE ON SCHEMA public TO app_user;
GRANT USAGE ON SCHEMA public TO platform_admin;

CREATE EXTENSION IF NOT EXISTS citext;

------------------------------------------------------------------------------
-- Migration version tracking (forward-only V00N__*.sql; the .NET runner checks
-- this table before applying a file; psql-applied migrations self-register at
-- the bottom of each file).
------------------------------------------------------------------------------
CREATE TABLE IF NOT EXISTS public.schema_migrations (
    version text NOT NULL,
    applied_at timestamptz NOT NULL DEFAULT now(),
    CONSTRAINT schema_migrations_pkey PRIMARY KEY (version)
);

GRANT SELECT ON public.schema_migrations TO app_user;
GRANT SELECT ON public.schema_migrations TO platform_admin;

------------------------------------------------------------------------------
-- app schema: RLS helpers + partition maintenance
------------------------------------------------------------------------------
CREATE SCHEMA app;

GRANT USAGE ON SCHEMA app TO app_user;
GRANT USAGE ON SCHEMA app TO platform_admin;

CREATE FUNCTION app.current_tenant_id()
RETURNS uuid
LANGUAGE sql
STABLE
AS $$
    SELECT coalesce(
        nullif(current_setting('app.tenant_id', true), ''),
        nullif(current_setting('app.current_tenant_id', true), '')
    )::uuid
$$;

COMMENT ON FUNCTION app.current_tenant_id() IS
'Tenant context for RLS. Reads app.tenant_id (spec §5) falling back to
app.current_tenant_id (set by RlsConnectionInterceptor). NULL when unset.';

CREATE FUNCTION app.is_platform_admin()
RETURNS boolean
LANGUAGE sql
STABLE
AS $$
    SELECT pg_has_role(current_user, 'platform_admin', 'member')
$$;

COMMENT ON FUNCTION app.is_platform_admin() IS
'Policy-based RLS bypass: true when the session role is a member of
platform_admin. app_user must never be granted platform_admin.';

-- Generic monthly-partition maintenance for RANGE (timestamptz) partitioned
-- tables (system.audit_log, identity_access.audit_logs). Creates partitions
-- named <table>_pYYYYMM from the current month through months_ahead. Called at
-- migration time and periodically by the system.jobs scheduler (see db/README.md).
CREATE FUNCTION app.ensure_month_partitions(parent regclass, months_ahead integer DEFAULT 3)
RETURNS void
LANGUAGE plpgsql
AS $$
DECLARE
    sch text;
    tbl text;
    month_start date;
    part_name text;
    i integer;
BEGIN
    SELECT n.nspname, c.relname
    INTO sch, tbl
    FROM pg_catalog.pg_class AS c
    INNER JOIN pg_catalog.pg_namespace AS n ON c.relnamespace = n.oid
    WHERE c.oid = parent;

    FOR i IN 0..months_ahead LOOP
        month_start := (date_trunc('month', now()) + make_interval(months => i))::date;
        part_name := format('%s_p%s', tbl, to_char(month_start, 'YYYYMM'));
        IF to_regclass(format('%I.%I', sch, part_name)) IS NULL THEN
            EXECUTE format(
                'CREATE TABLE %I.%I PARTITION OF %I.%I FOR VALUES FROM (%L) TO (%L)',
                sch, part_name, sch, tbl,
                month_start, (month_start + interval '1 month')::date
            );
        END IF;
    END LOOP;
END
$$;

------------------------------------------------------------------------------
-- tenancy schema
------------------------------------------------------------------------------
CREATE SCHEMA tenancy;

GRANT USAGE ON SCHEMA tenancy TO app_user;
GRANT USAGE ON SCHEMA tenancy TO platform_admin;

-- tenants: the ONE canonical tenants table (orchestrator ruling). Column shape
-- is exactly what wavio.SharedDataModel TenantConfiguration expects (the entity
-- is remapped from tenancy_org.tenants to tenancy.tenants by the dotnet dev).
-- status is free-form varchar (owned by the EF layer; no CHECK so seed values
-- can evolve without a migration).
CREATE TABLE tenancy.tenants (
    id uuid NOT NULL DEFAULT gen_random_uuid(),
    code varchar(50) NOT NULL,
    name varchar(200) NOT NULL,
    currency_code character(3) NOT NULL,
    country_code character(2) NOT NULL,
    timezone varchar(50) NOT NULL,
    status varchar(20) NOT NULL DEFAULT 'active',
    created_at timestamptz NOT NULL DEFAULT now(),
    updated_at timestamptz NOT NULL DEFAULT now(),
    created_by uuid,
    updated_by uuid,
    version integer NOT NULL DEFAULT 1,
    deleted_at timestamptz,
    CONSTRAINT tenants_pkey PRIMARY KEY (id)
);

-- Plain (not soft-delete-partial) unique: tenant codes are referenced by
-- verticals and API key prefixes and must never be reused, even after soft delete.
CREATE UNIQUE INDEX tenants_code_key ON tenancy.tenants (code);

COMMENT ON TABLE tenancy.tenants IS
'Canonical platform tenants (spec §5: tenant → waba(s) → phone_number(s)).
RLS scopes on id itself: a session sees only its own tenant row.';

-- tenant_settings: per-tenant configuration, keyed (category, setting_key).
CREATE TABLE tenancy.tenant_settings (
    id uuid NOT NULL DEFAULT gen_random_uuid(),
    tenant_id uuid NOT NULL,
    category varchar(50) NOT NULL,
    setting_key varchar(100) NOT NULL,
    setting_value jsonb NOT NULL,
    description text,
    created_at timestamptz NOT NULL DEFAULT now(),
    updated_at timestamptz NOT NULL DEFAULT now(),
    created_by uuid,
    updated_by uuid,
    version integer NOT NULL DEFAULT 1,
    CONSTRAINT tenant_settings_pkey PRIMARY KEY (id),
    CONSTRAINT tenant_settings_tenant_id_fkey FOREIGN KEY (tenant_id)
    REFERENCES tenancy.tenants (id) ON DELETE CASCADE,
    CONSTRAINT tenant_settings_tenant_id_category_setting_key_key
    UNIQUE (tenant_id, category, setting_key)
);

-- api_keys: tenant API keys (spec §5). The key itself is NEVER stored: only an
-- argon2id encoded hash (key_hash, e.g. '$argon2id$v=19$m=65536,t=3,p=4$...').
-- key_prefix is the short public head of the key (e.g. 'wav_live_3f2a91c0') used
-- to locate the row before verifying the full key against key_hash.
CREATE TABLE tenancy.api_keys (
    id uuid NOT NULL DEFAULT gen_random_uuid(),
    tenant_id uuid NOT NULL,
    name varchar(100) NOT NULL,
    key_prefix varchar(20) NOT NULL,
    key_hash text NOT NULL,
    scope varchar(20) NOT NULL,
    ip_allowlist inet [],
    last_used_at timestamptz,
    expires_at timestamptz,
    revoked_at timestamptz,
    revoked_by uuid,
    created_at timestamptz NOT NULL DEFAULT now(),
    updated_at timestamptz NOT NULL DEFAULT now(),
    created_by uuid,
    updated_by uuid,
    version integer NOT NULL DEFAULT 1,
    CONSTRAINT api_keys_pkey PRIMARY KEY (id),
    CONSTRAINT api_keys_tenant_id_fkey FOREIGN KEY (tenant_id)
    REFERENCES tenancy.tenants (id) ON DELETE CASCADE,
    CONSTRAINT api_keys_key_prefix_key UNIQUE (key_prefix),
    CONSTRAINT api_keys_scope_check
    CHECK (scope IN ('send_only', 'read_only', 'admin'))
);

CREATE INDEX api_keys_tenant_id_idx ON tenancy.api_keys (tenant_id);

COMMENT ON COLUMN tenancy.api_keys.ip_allowlist IS
'Optional CIDR/IP allowlist; NULL or empty = no IP restriction.';

-- external_tenant_refs: verticals (laundry, clinic, salon, ...) map their own
-- org models onto platform tenants (spec §5).
CREATE TABLE tenancy.external_tenant_refs (
    id uuid NOT NULL DEFAULT gen_random_uuid(),
    tenant_id uuid NOT NULL,
    vertical varchar(50) NOT NULL,
    external_ref varchar(200) NOT NULL,
    metadata jsonb NOT NULL DEFAULT '{}',
    created_at timestamptz NOT NULL DEFAULT now(),
    updated_at timestamptz NOT NULL DEFAULT now(),
    created_by uuid,
    updated_by uuid,
    CONSTRAINT external_tenant_refs_pkey PRIMARY KEY (id),
    CONSTRAINT external_tenant_refs_tenant_id_fkey FOREIGN KEY (tenant_id)
    REFERENCES tenancy.tenants (id) ON DELETE CASCADE,
    CONSTRAINT external_tenant_refs_vertical_external_ref_key
    UNIQUE (vertical, external_ref)
);

CREATE INDEX external_tenant_refs_tenant_id_idx
ON tenancy.external_tenant_refs (tenant_id);

------------------------------------------------------------------------------
-- Row-Level Security
------------------------------------------------------------------------------
ALTER TABLE tenancy.tenants ENABLE ROW LEVEL SECURITY;
ALTER TABLE tenancy.tenants FORCE ROW LEVEL SECURITY;

-- tenants scopes on its own id. Creating a tenant is a platform_admin (or
-- superuser/seeding) operation by construction: app_user with a tenant context
-- can never insert a row whose id differs from its own tenant id.
CREATE POLICY tenant_isolation ON tenancy.tenants
USING (id = app.current_tenant_id() OR app.is_platform_admin())
WITH CHECK (id = app.current_tenant_id() OR app.is_platform_admin());

ALTER TABLE tenancy.tenant_settings ENABLE ROW LEVEL SECURITY;
ALTER TABLE tenancy.tenant_settings FORCE ROW LEVEL SECURITY;

CREATE POLICY tenant_isolation ON tenancy.tenant_settings
USING (tenant_id = app.current_tenant_id() OR app.is_platform_admin())
WITH CHECK (tenant_id = app.current_tenant_id() OR app.is_platform_admin());

ALTER TABLE tenancy.api_keys ENABLE ROW LEVEL SECURITY;
ALTER TABLE tenancy.api_keys FORCE ROW LEVEL SECURITY;

CREATE POLICY tenant_isolation ON tenancy.api_keys
USING (tenant_id = app.current_tenant_id() OR app.is_platform_admin())
WITH CHECK (tenant_id = app.current_tenant_id() OR app.is_platform_admin());

ALTER TABLE tenancy.external_tenant_refs ENABLE ROW LEVEL SECURITY;
ALTER TABLE tenancy.external_tenant_refs FORCE ROW LEVEL SECURITY;

CREATE POLICY tenant_isolation ON tenancy.external_tenant_refs
USING (tenant_id = app.current_tenant_id() OR app.is_platform_admin())
WITH CHECK (tenant_id = app.current_tenant_id() OR app.is_platform_admin());

------------------------------------------------------------------------------
-- Grants
------------------------------------------------------------------------------
GRANT SELECT, INSERT, UPDATE, DELETE ON ALL TABLES IN SCHEMA tenancy TO app_user;
GRANT SELECT, INSERT, UPDATE, DELETE ON ALL TABLES IN SCHEMA tenancy TO platform_admin;

INSERT INTO public.schema_migrations (version)
VALUES ('V001')
ON CONFLICT (version) DO NOTHING;
