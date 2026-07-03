-- V004__system.sql
-- Schema: system — audit_log, feature_flags, jobs, job_runs (spec §6).
--
-- audit_log     : append-only (grants: SELECT + INSERT only, for app_user AND
--                 platform_admin — nobody rewrites history), monthly partitions.
-- feature_flags : platform-canonical flags table (orchestrator ruling: core
--                 identity's Kernel FeatureFlag entity is remapped HERE, so the
--                 column shape below matches FeatureFlagConfiguration exactly).
-- jobs/job_runs : deliberately NOT tenant-scoped — background workers are
--                 platform infrastructure that runs without a tenant context
--                 (they could never see their own queue under RLS). Jobs MAY
--                 target a tenant via nullable tenant_id.

CREATE SCHEMA system;

GRANT USAGE ON SCHEMA system TO app_user;
GRANT USAGE ON SCHEMA system TO platform_admin;

------------------------------------------------------------------------------
-- audit_log: every admin mutation, consent event and template submission
-- (spec §5). Partitioned monthly by occurred_at (append-only, unbounded
-- growth); partition key therefore joins the PK.
------------------------------------------------------------------------------
CREATE TABLE system.audit_log (
    id uuid NOT NULL DEFAULT gen_random_uuid(),
    occurred_at timestamptz NOT NULL DEFAULT now(),
    tenant_id uuid,
    actor_type varchar(20) NOT NULL,
    actor_user_id uuid,
    api_key_id uuid,
    actor_display varchar(200),
    action varchar(100) NOT NULL,
    resource_type varchar(50) NOT NULL,
    resource_id uuid,
    resource_display varchar(200),
    old_values jsonb,
    new_values jsonb,
    ip_address inet,
    user_agent text,
    request_id uuid,
    correlation_id uuid,
    success boolean NOT NULL DEFAULT TRUE,
    error_message text,
    created_at timestamptz NOT NULL DEFAULT now(),
    created_by uuid,
    CONSTRAINT audit_log_pkey PRIMARY KEY (id, occurred_at),
    CONSTRAINT audit_log_tenant_id_fkey FOREIGN KEY (tenant_id)
    REFERENCES tenancy.tenants (id) ON DELETE RESTRICT,
    CONSTRAINT audit_log_api_key_id_fkey FOREIGN KEY (api_key_id)
    REFERENCES tenancy.api_keys (id) ON DELETE SET NULL,
    CONSTRAINT audit_log_actor_type_check CHECK (
        actor_type IN ('user', 'api_key', 'system')
    )
) PARTITION BY RANGE (occurred_at);

-- actor_user_id FK is added by V005 once identity_access.users exists.

CREATE TABLE system.audit_log_pdefault PARTITION OF system.audit_log DEFAULT;

SELECT app.ensure_month_partitions('system.audit_log', 3);

CREATE INDEX audit_log_tenant_id_occurred_at_idx
ON system.audit_log (tenant_id, occurred_at DESC);
CREATE INDEX audit_log_actor_user_id_idx ON system.audit_log (actor_user_id);
CREATE INDEX audit_log_resource_type_resource_id_idx
ON system.audit_log (resource_type, resource_id);

COMMENT ON TABLE system.audit_log IS
'Append-only platform audit trail (spec §5). No UPDATE/DELETE granted to any
application role. resource_id is polymorphic (resource_type discriminator) —
deliberately no FK. Monthly partitions via app.ensure_month_partitions().';

------------------------------------------------------------------------------
-- feature_flags: canonical platform flags. Shape = Kernel/FeatureFlagConfiguration
-- (core identity remaps its FeatureFlag entity to this table — do NOT diverge).
-- tenant_id NULL = global flag visible to every tenant.
------------------------------------------------------------------------------
CREATE TABLE system.feature_flags (
    id uuid NOT NULL DEFAULT gen_random_uuid(),
    tenant_id uuid,
    flag_key varchar(100) NOT NULL,
    name varchar(200) NOT NULL,
    description text,
    flag_type varchar(20) NOT NULL,
    default_value boolean NOT NULL DEFAULT FALSE,
    is_enabled boolean NOT NULL DEFAULT FALSE,
    rollout_percent smallint,
    target_segments text [],
    target_user_ids uuid [],
    target_cities text [],
    variants jsonb,
    starts_at timestamptz,
    ends_at timestamptz,
    last_evaluated_at timestamptz,
    evaluation_count bigint NOT NULL DEFAULT 0,
    metadata jsonb NOT NULL DEFAULT '{}',
    created_at timestamptz NOT NULL DEFAULT now(),
    updated_at timestamptz NOT NULL DEFAULT now(),
    created_by uuid,
    updated_by uuid,
    status varchar(20) NOT NULL DEFAULT 'active',
    CONSTRAINT feature_flags_pkey PRIMARY KEY (id),
    CONSTRAINT feature_flags_tenant_id_fkey FOREIGN KEY (tenant_id)
    REFERENCES tenancy.tenants (id) ON DELETE CASCADE,
    CONSTRAINT feature_flags_rollout_percent_check CHECK (
        rollout_percent IS NULL OR rollout_percent BETWEEN 0 AND 100
    )
);

-- NULLS NOT DISTINCT: two global flags (tenant_id NULL) with the same key must
-- also collide, which a plain unique index would not enforce.
CREATE UNIQUE INDEX feature_flags_tenant_id_flag_key_key
ON system.feature_flags (tenant_id, flag_key) NULLS NOT DISTINCT;

------------------------------------------------------------------------------
-- jobs: scheduled/background job definitions. job_runs: execution history.
------------------------------------------------------------------------------
CREATE TABLE system.jobs (
    id uuid NOT NULL DEFAULT gen_random_uuid(),
    job_key varchar(100) NOT NULL,
    job_type varchar(100) NOT NULL,
    description text,
    tenant_id uuid,
    schedule_cron varchar(100),
    payload jsonb NOT NULL DEFAULT '{}',
    is_enabled boolean NOT NULL DEFAULT TRUE,
    max_attempts smallint NOT NULL DEFAULT 3,
    timeout_seconds integer NOT NULL DEFAULT 300,
    next_run_at timestamptz,
    last_run_at timestamptz,
    created_at timestamptz NOT NULL DEFAULT now(),
    updated_at timestamptz NOT NULL DEFAULT now(),
    created_by uuid,
    updated_by uuid,
    version integer NOT NULL DEFAULT 1,
    CONSTRAINT jobs_pkey PRIMARY KEY (id),
    CONSTRAINT jobs_job_key_key UNIQUE (job_key),
    CONSTRAINT jobs_tenant_id_fkey FOREIGN KEY (tenant_id)
    REFERENCES tenancy.tenants (id) ON DELETE CASCADE
);

CREATE INDEX jobs_due_idx
ON system.jobs (next_run_at)
WHERE is_enabled = TRUE;

COMMENT ON COLUMN system.jobs.schedule_cron IS
'Cron expression for recurring jobs; NULL = on-demand/one-shot.';

CREATE TABLE system.job_runs (
    id uuid NOT NULL DEFAULT gen_random_uuid(),
    job_id uuid NOT NULL,
    attempt smallint NOT NULL DEFAULT 1,
    status varchar(20) NOT NULL DEFAULT 'running',
    started_at timestamptz NOT NULL DEFAULT now(),
    finished_at timestamptz,
    error text,
    output jsonb,
    created_at timestamptz NOT NULL DEFAULT now(),
    created_by uuid,
    CONSTRAINT job_runs_pkey PRIMARY KEY (id),
    CONSTRAINT job_runs_job_id_fkey FOREIGN KEY (job_id)
    REFERENCES system.jobs (id) ON DELETE CASCADE,
    CONSTRAINT job_runs_status_check CHECK (
        status IN ('running', 'succeeded', 'failed', 'timed_out', 'cancelled')
    )
);

CREATE INDEX job_runs_job_id_started_at_idx
ON system.job_runs (job_id, started_at DESC);

------------------------------------------------------------------------------
-- Row-Level Security
--   audit_log     : nullable-tenant pattern (NULL = platform-level entry).
--   feature_flags : nullable-tenant pattern (NULL = global flag). Single FOR ALL
--                   policy so evaluation-counter updates on global flags keep
--                   working for tenant sessions; flag administration is guarded
--                   by app-layer RBAC (platform_admin-only endpoints).
--   jobs/job_runs : NO RLS (platform infrastructure — see file header).
------------------------------------------------------------------------------
ALTER TABLE system.audit_log ENABLE ROW LEVEL SECURITY;
ALTER TABLE system.audit_log FORCE ROW LEVEL SECURITY;

CREATE POLICY tenant_isolation ON system.audit_log
USING (
    tenant_id IS NULL
    OR tenant_id = app.current_tenant_id()
    OR app.is_platform_admin()
)
WITH CHECK (
    tenant_id IS NULL
    OR tenant_id = app.current_tenant_id()
    OR app.is_platform_admin()
);

ALTER TABLE system.feature_flags ENABLE ROW LEVEL SECURITY;
ALTER TABLE system.feature_flags FORCE ROW LEVEL SECURITY;

CREATE POLICY tenant_isolation ON system.feature_flags
USING (
    tenant_id IS NULL
    OR tenant_id = app.current_tenant_id()
    OR app.is_platform_admin()
)
WITH CHECK (
    tenant_id IS NULL
    OR tenant_id = app.current_tenant_id()
    OR app.is_platform_admin()
);

------------------------------------------------------------------------------
-- Grants. Per-table on purpose: audit_log is append-only (no UPDATE/DELETE for
-- ANY application role, platform_admin included), and partitions themselves get
-- no direct grants — all access goes through the parent table.
------------------------------------------------------------------------------
GRANT SELECT, INSERT ON system.audit_log TO app_user;
GRANT SELECT, INSERT ON system.audit_log TO platform_admin;
GRANT SELECT, INSERT, UPDATE, DELETE ON system.feature_flags TO app_user;
GRANT SELECT, INSERT, UPDATE, DELETE ON system.feature_flags TO platform_admin;
GRANT SELECT, INSERT, UPDATE, DELETE ON system.jobs TO app_user;
GRANT SELECT, INSERT, UPDATE, DELETE ON system.jobs TO platform_admin;
GRANT SELECT, INSERT, UPDATE, DELETE ON system.job_runs TO app_user;
GRANT SELECT, INSERT, UPDATE, DELETE ON system.job_runs TO platform_admin;

INSERT INTO public.schema_migrations (version)
VALUES ('V004')
ON CONFLICT (version) DO NOTHING;
