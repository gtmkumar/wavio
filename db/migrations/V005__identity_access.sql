-- V005__identity_access.sql
-- Schema: identity_access — owned by core identity (wavio.SharedDataModel).
-- DDL is derived 1:1 from Persistence/Configurations/IdentityAccess/*.cs:
-- table/column names, types, nullability, unique index names and FK names all
-- match the EF model. Core identity is database-first and must boot against
-- this DDL unchanged.
--
-- Tenancy model here: users/permissions/tokens are PLATFORM-GLOBAL (no
-- tenant_id column — a user may belong to several tenants via
-- user_scope_memberships). These tables are therefore deliberately not
-- RLS-scoped; access control is app-layer (deny-wins RBAC, spec §5). The two
-- tables that DO carry tenant_id (roles, audit_logs) get the nullable-tenant
-- RLS pattern (NULL = system/platform row).
--
-- Table-name exceptions to the plural convention (EF-mandated, database-first):
-- login_history, user_permission_override.

CREATE SCHEMA identity_access;

GRANT USAGE ON SCHEMA identity_access TO app_user;
GRANT USAGE ON SCHEMA identity_access TO platform_admin;

------------------------------------------------------------------------------
-- users
------------------------------------------------------------------------------
CREATE TABLE identity_access.users (
    id uuid NOT NULL DEFAULT gen_random_uuid(),
    phone_e164 varchar(20),
    email citext,
    password_hash text,
    password_changed_at timestamptz,
    must_change_password boolean NOT NULL DEFAULT FALSE,
    mfa_enabled boolean NOT NULL DEFAULT FALSE,
    mfa_secret text,
    mfa_backup_codes text [],
    user_type varchar(30) NOT NULL,
    locale varchar(10) NOT NULL DEFAULT 'en-IN',
    timezone varchar(50) NOT NULL DEFAULT 'Asia/Kolkata',
    status varchar(20) NOT NULL DEFAULT 'active',
    last_login_at timestamptz,
    last_login_ip inet,
    last_active_at timestamptz,
    failed_attempts smallint NOT NULL DEFAULT 0,
    locked_until timestamptz,
    email_verified_at timestamptz,
    phone_verified_at timestamptz,
    invitation_token text,
    invitation_sent_at timestamptz,
    invitation_accepted_at timestamptz,
    created_at timestamptz NOT NULL DEFAULT now(),
    updated_at timestamptz NOT NULL DEFAULT now(),
    created_by uuid,
    updated_by uuid,
    version integer NOT NULL DEFAULT 1,
    perm_version integer NOT NULL DEFAULT 1,
    deleted_at timestamptz,
    CONSTRAINT users_pkey PRIMARY KEY (id)
);

-- Partial on live rows: a soft-deleted account must not block re-registration
-- with the same email/phone. EF never validates index shape at runtime.
CREATE UNIQUE INDEX users_email_key
ON identity_access.users (email)
WHERE deleted_at IS NULL;
CREATE UNIQUE INDEX users_phone_e164_key
ON identity_access.users (phone_e164)
WHERE deleted_at IS NULL;

------------------------------------------------------------------------------
-- user_profiles (1:1, PK = FK). pan_number / bank_account_number / upi_id hold
-- AES-256-GCM ciphertext (PiiValueConverter) — text, not constrained lengths.
------------------------------------------------------------------------------
CREATE TABLE identity_access.user_profiles (
    user_id uuid NOT NULL,
    first_name varchar(100),
    last_name varchar(100),
    display_name varchar(200),
    avatar_url text,
    date_of_birth date,
    gender varchar(20),
    designation varchar(100),
    department varchar(100),
    employee_id varchar(50),
    joined_at date,
    emergency_contact_name varchar(200),
    emergency_contact_phone varchar(20),
    address jsonb,
    employment_type varchar(20),
    pan_number text,
    aadhaar_number_masked varchar(20),
    kyc_status varchar(20),
    kyc_verified_at timestamptz,
    bank_account_name varchar(200),
    bank_account_number text,
    bank_ifsc varchar(11),
    upi_id text,
    fcm_token text,
    fcm_token_updated_at timestamptz,
    apns_token text,
    apns_token_updated_at timestamptz,
    preferences jsonb NOT NULL DEFAULT '{}',
    metadata jsonb NOT NULL DEFAULT '{}',
    created_at timestamptz NOT NULL DEFAULT now(),
    updated_at timestamptz NOT NULL DEFAULT now(),
    created_by uuid,
    updated_by uuid,
    status varchar(20) NOT NULL DEFAULT 'active',
    CONSTRAINT user_profiles_pkey PRIMARY KEY (user_id),
    CONSTRAINT user_profiles_user_id_fkey FOREIGN KEY (user_id)
    REFERENCES identity_access.users (id) ON DELETE CASCADE
);

------------------------------------------------------------------------------
-- roles (tenant_id NULL = system role shared by all tenants)
------------------------------------------------------------------------------
CREATE TABLE identity_access.roles (
    id uuid NOT NULL DEFAULT gen_random_uuid(),
    tenant_id uuid,
    code varchar(50) NOT NULL,
    name varchar(100) NOT NULL,
    description text,
    scope_type varchar(20) NOT NULL,
    is_system boolean NOT NULL DEFAULT FALSE,
    is_assignable boolean NOT NULL DEFAULT TRUE,
    priority smallint NOT NULL DEFAULT 0,
    created_at timestamptz NOT NULL DEFAULT now(),
    updated_at timestamptz NOT NULL DEFAULT now(),
    created_by uuid,
    updated_by uuid,
    deleted_at timestamptz,
    status varchar(20) NOT NULL DEFAULT 'active',
    CONSTRAINT roles_pkey PRIMARY KEY (id),
    CONSTRAINT roles_tenant_id_fkey FOREIGN KEY (tenant_id)
    REFERENCES tenancy.tenants (id) ON DELETE CASCADE
);

-- NULLS NOT DISTINCT so two system roles (tenant_id NULL) cannot share a code.
CREATE UNIQUE INDEX roles_tenant_id_code_key
ON identity_access.roles (tenant_id, code) NULLS NOT DISTINCT;

------------------------------------------------------------------------------
-- permissions (platform-global catalog)
------------------------------------------------------------------------------
CREATE TABLE identity_access.permissions (
    id uuid NOT NULL DEFAULT gen_random_uuid(),
    code varchar(100) NOT NULL,
    module varchar(50) NOT NULL,
    module_key varchar(64),
    action varchar(50) NOT NULL,
    name varchar(200) NOT NULL,
    description text,
    is_system boolean NOT NULL DEFAULT FALSE,
    requires_scope boolean NOT NULL DEFAULT FALSE,
    risk_level varchar(20) NOT NULL DEFAULT 'low',
    created_at timestamptz NOT NULL DEFAULT now(),
    updated_at timestamptz NOT NULL DEFAULT now(),
    created_by uuid,
    updated_by uuid,
    status varchar(20) NOT NULL DEFAULT 'active',
    CONSTRAINT permissions_pkey PRIMARY KEY (id)
);

CREATE UNIQUE INDEX permissions_code_key ON identity_access.permissions (code);

------------------------------------------------------------------------------
-- role_permissions
------------------------------------------------------------------------------
CREATE TABLE identity_access.role_permissions (
    id uuid NOT NULL DEFAULT gen_random_uuid(),
    role_id uuid NOT NULL,
    permission_id uuid NOT NULL,
    effect varchar(16) NOT NULL DEFAULT 'allow',
    granted_at timestamptz NOT NULL DEFAULT now(),
    granted_by uuid,
    created_at timestamptz NOT NULL DEFAULT now(),
    created_by uuid,
    CONSTRAINT role_permissions_pkey PRIMARY KEY (id),
    CONSTRAINT role_permissions_role_id_fkey FOREIGN KEY (role_id)
    REFERENCES identity_access.roles (id) ON DELETE CASCADE,
    CONSTRAINT role_permissions_permission_id_fkey FOREIGN KEY (permission_id)
    REFERENCES identity_access.permissions (id) ON DELETE CASCADE,
    CONSTRAINT role_permissions_effect_check CHECK (effect IN ('allow', 'deny'))
);

CREATE UNIQUE INDEX role_permissions_role_id_permission_id_key
ON identity_access.role_permissions (role_id, permission_id);

------------------------------------------------------------------------------
-- user_permission_override (singular — EF-mandated). Surrogate PK; the natural
-- key includes two nullable scope columns, enforced via COALESCE expression
-- index (per the EF configuration comment).
------------------------------------------------------------------------------
CREATE TABLE identity_access.user_permission_override (
    id uuid NOT NULL DEFAULT gen_random_uuid(),
    user_id uuid NOT NULL,
    permission_id uuid NOT NULL,
    effect varchar(16) NOT NULL DEFAULT 'allow',
    scope_type varchar(20),
    scope_id uuid,
    reason text,
    expires_at timestamptz,
    granted_at timestamptz NOT NULL DEFAULT now(),
    granted_by uuid,
    created_at timestamptz NOT NULL DEFAULT now(),
    CONSTRAINT user_permission_override_pkey PRIMARY KEY (id),
    CONSTRAINT user_permission_override_user_id_fkey FOREIGN KEY (user_id)
    REFERENCES identity_access.users (id) ON DELETE CASCADE,
    CONSTRAINT user_permission_override_permission_id_fkey
    FOREIGN KEY (permission_id)
    REFERENCES identity_access.permissions (id) ON DELETE CASCADE,
    CONSTRAINT user_permission_override_effect_check
    CHECK (effect IN ('allow', 'deny'))
);

CREATE UNIQUE INDEX user_permission_override_natural_key
ON identity_access.user_permission_override (
    user_id,
    permission_id,
    coalesce(scope_type, ''),
    coalesce(scope_id, '00000000-0000-0000-0000-000000000000')
);

CREATE INDEX ix_user_permission_override_user_id
ON identity_access.user_permission_override (user_id);

------------------------------------------------------------------------------
-- user_scope_memberships (scope_id is polymorphic per scope_type — e.g. a
-- tenancy.tenants id when scope_type = 'tenant' — so no FK: documented
-- exclusion for the FK-audit gate).
------------------------------------------------------------------------------
CREATE TABLE identity_access.user_scope_memberships (
    id uuid NOT NULL DEFAULT gen_random_uuid(),
    user_id uuid NOT NULL,
    scope_type varchar(20) NOT NULL,
    scope_id uuid,
    role_id uuid NOT NULL,
    is_primary boolean NOT NULL DEFAULT FALSE,
    granted_at timestamptz NOT NULL DEFAULT now(),
    granted_by uuid,
    revoked_at timestamptz,
    revoked_by uuid,
    revoked_reason text,
    expires_at timestamptz,
    metadata jsonb NOT NULL DEFAULT '{}',
    created_at timestamptz NOT NULL DEFAULT now(),
    created_by uuid,
    CONSTRAINT user_scope_memberships_pkey PRIMARY KEY (id),
    CONSTRAINT user_scope_memberships_user_id_fkey FOREIGN KEY (user_id)
    REFERENCES identity_access.users (id) ON DELETE CASCADE,
    CONSTRAINT user_scope_memberships_role_id_fkey FOREIGN KEY (role_id)
    REFERENCES identity_access.roles (id) ON DELETE RESTRICT
);

CREATE UNIQUE INDEX user_scope_memberships_user_id_scope_type_scope_id_role_id_key
ON identity_access.user_scope_memberships (
    user_id, scope_type, scope_id, role_id
) NULLS NOT DISTINCT;

------------------------------------------------------------------------------
-- refresh_tokens (rotation families; family_id/parent_token_id self-FKs)
------------------------------------------------------------------------------
CREATE TABLE identity_access.refresh_tokens (
    id uuid NOT NULL DEFAULT gen_random_uuid(),
    user_id uuid,
    token_hash text NOT NULL,
    family_id uuid NOT NULL,
    parent_token_id uuid,
    device_id varchar(255),
    device_name varchar(200),
    device_os varchar(50),
    ip_address inet,
    user_agent text,
    issued_at timestamptz NOT NULL DEFAULT now(),
    expires_at timestamptz NOT NULL,
    last_used_at timestamptz,
    revoked_at timestamptz,
    revoked_reason varchar(50),
    created_at timestamptz NOT NULL DEFAULT now(),
    CONSTRAINT refresh_tokens_pkey PRIMARY KEY (id),
    CONSTRAINT refresh_tokens_user_id_fkey FOREIGN KEY (user_id)
    REFERENCES identity_access.users (id) ON DELETE CASCADE,
    CONSTRAINT refresh_tokens_family_id_fkey FOREIGN KEY (family_id)
    REFERENCES identity_access.refresh_tokens (id),
    CONSTRAINT refresh_tokens_parent_token_id_fkey FOREIGN KEY (parent_token_id)
    REFERENCES identity_access.refresh_tokens (id)
);

CREATE UNIQUE INDEX refresh_tokens_token_hash_key
ON identity_access.refresh_tokens (token_hash);
CREATE INDEX refresh_tokens_user_id_idx
ON identity_access.refresh_tokens (user_id);
CREATE INDEX refresh_tokens_family_id_idx
ON identity_access.refresh_tokens (family_id);
CREATE INDEX refresh_tokens_parent_token_id_idx
ON identity_access.refresh_tokens (parent_token_id);

------------------------------------------------------------------------------
-- password_resets
------------------------------------------------------------------------------
CREATE TABLE identity_access.password_resets (
    id uuid NOT NULL DEFAULT gen_random_uuid(),
    user_id uuid,
    token_hash text NOT NULL,
    requested_ip inet,
    requested_user_agent text,
    used_at timestamptz,
    used_ip inet,
    expires_at timestamptz NOT NULL,
    created_at timestamptz NOT NULL DEFAULT now(),
    updated_at timestamptz NOT NULL DEFAULT now(),
    created_by uuid,
    updated_by uuid,
    status varchar(20) NOT NULL DEFAULT 'pending',
    CONSTRAINT password_resets_pkey PRIMARY KEY (id),
    CONSTRAINT password_resets_user_id_fkey FOREIGN KEY (user_id)
    REFERENCES identity_access.users (id) ON DELETE CASCADE
);

CREATE UNIQUE INDEX password_resets_token_hash_key
ON identity_access.password_resets (token_hash);
CREATE INDEX password_resets_user_id_idx
ON identity_access.password_resets (user_id);

------------------------------------------------------------------------------
-- otp_codes (reference_id is polymorphic per reference_type — documented
-- FK-audit exclusion)
------------------------------------------------------------------------------
CREATE TABLE identity_access.otp_codes (
    id uuid NOT NULL DEFAULT gen_random_uuid(),
    purpose varchar(30) NOT NULL,
    identifier varchar(255) NOT NULL,
    identifier_type varchar(10) NOT NULL,
    code_hash text NOT NULL,
    code_salt text,
    user_id uuid,
    reference_id uuid,
    reference_type varchar(50),
    attempts smallint NOT NULL DEFAULT 0,
    max_attempts smallint NOT NULL DEFAULT 5,
    verified_at timestamptz,
    expires_at timestamptz NOT NULL,
    ip_address inet,
    user_agent text,
    created_at timestamptz NOT NULL DEFAULT now(),
    CONSTRAINT otp_codes_pkey PRIMARY KEY (id),
    CONSTRAINT otp_codes_user_id_fkey FOREIGN KEY (user_id)
    REFERENCES identity_access.users (id) ON DELETE CASCADE
);

CREATE INDEX otp_codes_identifier_purpose_idx
ON identity_access.otp_codes (identifier, purpose);
CREATE INDEX otp_codes_user_id_idx ON identity_access.otp_codes (user_id);

------------------------------------------------------------------------------
-- login_history (singular — EF-mandated; append-only usage)
------------------------------------------------------------------------------
CREATE TABLE identity_access.login_history (
    id uuid NOT NULL DEFAULT gen_random_uuid(),
    user_id uuid,
    identifier varchar(255) NOT NULL,
    auth_method varchar(20) NOT NULL,
    success boolean NOT NULL,
    failure_reason varchar(100),
    ip_address inet,
    user_agent text,
    device_id varchar(255),
    country_code character(2),
    city varchar(100),
    is_suspicious boolean NOT NULL DEFAULT FALSE,
    risk_score smallint,
    occurred_at timestamptz NOT NULL DEFAULT now(),
    created_at timestamptz NOT NULL DEFAULT now(),
    created_by uuid,
    CONSTRAINT login_history_pkey PRIMARY KEY (id),
    CONSTRAINT login_history_user_id_fkey FOREIGN KEY (user_id)
    REFERENCES identity_access.users (id) ON DELETE SET NULL
);

CREATE INDEX login_history_user_id_occurred_at_idx
ON identity_access.login_history (user_id, occurred_at DESC);

------------------------------------------------------------------------------
-- audit_logs: core identity's own audit trail. Composite PK (id, occurred_at)
-- is REQUIRED by the EF configuration ("Composite PK required by PG range
-- partitioning on occurred_at") — partitioned monthly like system.audit_log.
-- The actor_user_id FK is not mapped in EF (only Tenant is) but is required by
-- the FK-audit gate; extra DB-side FKs are invisible to EF at runtime.
------------------------------------------------------------------------------
CREATE TABLE identity_access.audit_logs (
    id uuid NOT NULL DEFAULT gen_random_uuid(),
    occurred_at timestamptz NOT NULL DEFAULT now(),
    tenant_id uuid,
    actor_user_id uuid,
    actor_type varchar(20) NOT NULL,
    actor_display varchar(200),
    action varchar(100) NOT NULL,
    resource_type varchar(50) NOT NULL,
    resource_id uuid,
    resource_display varchar(200),
    old_values jsonb,
    new_values jsonb,
    changed_fields text [],
    ip_address inet,
    user_agent text,
    request_id uuid,
    correlation_id uuid,
    success boolean NOT NULL DEFAULT TRUE,
    error_message text,
    created_at timestamptz NOT NULL DEFAULT now(),
    created_by uuid,
    CONSTRAINT audit_logs_pkey PRIMARY KEY (id, occurred_at),
    CONSTRAINT audit_logs_tenant_id_fkey FOREIGN KEY (tenant_id)
    REFERENCES tenancy.tenants (id) ON DELETE RESTRICT,
    CONSTRAINT audit_logs_actor_user_id_fkey FOREIGN KEY (actor_user_id)
    REFERENCES identity_access.users (id) ON DELETE SET NULL
) PARTITION BY RANGE (occurred_at);

CREATE TABLE identity_access.audit_logs_pdefault
PARTITION OF identity_access.audit_logs DEFAULT;

SELECT app.ensure_month_partitions('identity_access.audit_logs', 3);

CREATE INDEX audit_logs_tenant_id_occurred_at_idx
ON identity_access.audit_logs (tenant_id, occurred_at DESC);
CREATE INDEX audit_logs_actor_user_id_idx
ON identity_access.audit_logs (actor_user_id);

------------------------------------------------------------------------------
-- Cross-schema FK deferred from V004 (users did not exist yet).
------------------------------------------------------------------------------
ALTER TABLE system.audit_log
ADD CONSTRAINT audit_log_actor_user_id_fkey FOREIGN KEY (actor_user_id)
REFERENCES identity_access.users (id) ON DELETE SET NULL;

------------------------------------------------------------------------------
-- Row-Level Security: only roles and audit_logs carry tenant_id
-- (nullable-tenant pattern; NULL = system role / platform-level audit entry).
------------------------------------------------------------------------------
ALTER TABLE identity_access.roles ENABLE ROW LEVEL SECURITY;
ALTER TABLE identity_access.roles FORCE ROW LEVEL SECURITY;

CREATE POLICY tenant_isolation ON identity_access.roles
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

ALTER TABLE identity_access.audit_logs ENABLE ROW LEVEL SECURITY;
ALTER TABLE identity_access.audit_logs FORCE ROW LEVEL SECURITY;

CREATE POLICY tenant_isolation ON identity_access.audit_logs
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
-- Grants (audit_logs append-only: SELECT + INSERT, no UPDATE/DELETE for anyone)
------------------------------------------------------------------------------
GRANT SELECT, INSERT, UPDATE, DELETE ON
identity_access.users,
identity_access.user_profiles,
identity_access.roles,
identity_access.permissions,
identity_access.role_permissions,
identity_access.user_permission_override,
identity_access.user_scope_memberships,
identity_access.refresh_tokens,
identity_access.password_resets,
identity_access.otp_codes,
identity_access.login_history
TO app_user, platform_admin;

GRANT SELECT, INSERT ON identity_access.audit_logs TO app_user, platform_admin;

INSERT INTO public.schema_migrations (version)
VALUES ('V005')
ON CONFLICT (version) DO NOTHING;
