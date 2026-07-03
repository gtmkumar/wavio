-- V002__waba.sql
-- Schema: waba — business_accounts, phone_numbers, phone_number_events,
-- currency_migrations, business_profiles (spec §6).
--
-- All five tables are tenant-scoped (NOT NULL tenant_id + RLS, spec §5).
-- External Meta identifiers are stored as varchar columns prefixed meta_*;
-- they end in _id but are NOT uuid reference columns — the FK-audit gate only
-- requires FKs on uuid *_id columns (see db/README.md, "FK audit rules").

CREATE SCHEMA waba;

GRANT USAGE ON SCHEMA waba TO app_user;
GRANT USAGE ON SCHEMA waba TO platform_admin;

------------------------------------------------------------------------------
-- business_accounts: one row per Meta WhatsApp Business Account (WABA).
------------------------------------------------------------------------------
CREATE TABLE waba.business_accounts (
    id uuid NOT NULL DEFAULT gen_random_uuid(),
    tenant_id uuid NOT NULL,
    meta_waba_id varchar(32) NOT NULL,
    name varchar(200) NOT NULL,
    currency_code character(3),
    message_template_namespace varchar(100),
    -- Meta system-user token, envelope-encrypted at the app layer (spec §5:
    -- master key on VPS, per-value data keys). Never stored in plaintext.
    system_user_token_ciphertext text,
    token_key_ref varchar(100),
    status varchar(20) NOT NULL DEFAULT 'active',
    created_at timestamptz NOT NULL DEFAULT now(),
    updated_at timestamptz NOT NULL DEFAULT now(),
    created_by uuid,
    updated_by uuid,
    version integer NOT NULL DEFAULT 1,
    CONSTRAINT business_accounts_pkey PRIMARY KEY (id),
    CONSTRAINT business_accounts_tenant_id_fkey FOREIGN KEY (tenant_id)
    REFERENCES tenancy.tenants (id) ON DELETE RESTRICT,
    CONSTRAINT business_accounts_meta_waba_id_key UNIQUE (meta_waba_id)
);

CREATE INDEX business_accounts_tenant_id_idx ON waba.business_accounts (tenant_id);

------------------------------------------------------------------------------
-- phone_numbers: WhatsApp phone numbers under a WABA. status is a constrained
-- state machine (values enforced by CHECK; transitions documented below and
-- validated at the app layer + recorded in phone_number_events).
------------------------------------------------------------------------------
CREATE TABLE waba.phone_numbers (
    id uuid NOT NULL DEFAULT gen_random_uuid(),
    tenant_id uuid NOT NULL,
    business_account_id uuid NOT NULL,
    meta_phone_number_id varchar(32) NOT NULL,
    display_phone_number varchar(20) NOT NULL,
    verified_name varchar(200),
    status varchar(20) NOT NULL DEFAULT 'PENDING',
    quality_rating varchar(10),
    messaging_tier varchar(20),
    created_at timestamptz NOT NULL DEFAULT now(),
    updated_at timestamptz NOT NULL DEFAULT now(),
    created_by uuid,
    updated_by uuid,
    version integer NOT NULL DEFAULT 1,
    CONSTRAINT phone_numbers_pkey PRIMARY KEY (id),
    CONSTRAINT phone_numbers_tenant_id_fkey FOREIGN KEY (tenant_id)
    REFERENCES tenancy.tenants (id) ON DELETE RESTRICT,
    CONSTRAINT phone_numbers_business_account_id_fkey FOREIGN KEY (business_account_id)
    REFERENCES waba.business_accounts (id) ON DELETE CASCADE,
    CONSTRAINT phone_numbers_meta_phone_number_id_key UNIQUE (meta_phone_number_id),
    CONSTRAINT phone_numbers_status_check CHECK (
        status IN ('PENDING', 'CONNECTED', 'FLAGGED', 'RESTRICTED', 'BANNED')
    ),
    CONSTRAINT phone_numbers_quality_rating_check CHECK (
        quality_rating IS NULL
        OR quality_rating IN ('GREEN', 'YELLOW', 'RED', 'UNKNOWN')
    )
);

CREATE INDEX phone_numbers_tenant_id_idx ON waba.phone_numbers (tenant_id);
CREATE INDEX phone_numbers_business_account_id_idx
ON waba.phone_numbers (business_account_id);

COMMENT ON COLUMN waba.phone_numbers.status IS
'State machine (valid values CHECK-enforced; transitions app-enforced and
logged in phone_number_events):
  PENDING    -> CONNECTED
  CONNECTED  -> FLAGGED | RESTRICTED | BANNED
  FLAGGED    -> CONNECTED | RESTRICTED | BANNED
  RESTRICTED -> CONNECTED | BANNED
  BANNED     -> (terminal)';

------------------------------------------------------------------------------
-- phone_number_events: append-only history of status/quality/tier changes
-- (created_* pair only — rows are never updated).
------------------------------------------------------------------------------
CREATE TABLE waba.phone_number_events (
    id uuid NOT NULL DEFAULT gen_random_uuid(),
    tenant_id uuid NOT NULL,
    phone_number_id uuid NOT NULL,
    event_type varchar(50) NOT NULL,
    old_status varchar(20),
    new_status varchar(20),
    payload jsonb,
    occurred_at timestamptz NOT NULL DEFAULT now(),
    created_at timestamptz NOT NULL DEFAULT now(),
    created_by uuid,
    CONSTRAINT phone_number_events_pkey PRIMARY KEY (id),
    CONSTRAINT phone_number_events_tenant_id_fkey FOREIGN KEY (tenant_id)
    REFERENCES tenancy.tenants (id) ON DELETE RESTRICT,
    CONSTRAINT phone_number_events_phone_number_id_fkey FOREIGN KEY (phone_number_id)
    REFERENCES waba.phone_numbers (id) ON DELETE CASCADE
);

CREATE INDEX phone_number_events_tenant_id_idx
ON waba.phone_number_events (tenant_id);
CREATE INDEX phone_number_events_phone_number_id_occurred_at_idx
ON waba.phone_number_events (phone_number_id, occurred_at DESC);

------------------------------------------------------------------------------
-- currency_migrations: Meta-driven WABA currency migrations (spec §2.4).
------------------------------------------------------------------------------
CREATE TABLE waba.currency_migrations (
    id uuid NOT NULL DEFAULT gen_random_uuid(),
    tenant_id uuid NOT NULL,
    business_account_id uuid NOT NULL,
    old_currency character(3) NOT NULL,
    new_currency character(3) NOT NULL,
    scheduled_for date,
    completed_at timestamptz,
    status varchar(20) NOT NULL DEFAULT 'scheduled',
    payload jsonb,
    created_at timestamptz NOT NULL DEFAULT now(),
    updated_at timestamptz NOT NULL DEFAULT now(),
    created_by uuid,
    updated_by uuid,
    CONSTRAINT currency_migrations_pkey PRIMARY KEY (id),
    CONSTRAINT currency_migrations_tenant_id_fkey FOREIGN KEY (tenant_id)
    REFERENCES tenancy.tenants (id) ON DELETE RESTRICT,
    CONSTRAINT currency_migrations_business_account_id_fkey
    FOREIGN KEY (business_account_id)
    REFERENCES waba.business_accounts (id) ON DELETE CASCADE,
    CONSTRAINT currency_migrations_status_check CHECK (
        status IN ('scheduled', 'in_progress', 'completed', 'cancelled')
    )
);

CREATE INDEX currency_migrations_tenant_id_idx
ON waba.currency_migrations (tenant_id);
CREATE INDEX currency_migrations_business_account_id_idx
ON waba.currency_migrations (business_account_id);

------------------------------------------------------------------------------
-- business_profiles: the public WhatsApp business profile, 1:1 per phone number.
------------------------------------------------------------------------------
CREATE TABLE waba.business_profiles (
    id uuid NOT NULL DEFAULT gen_random_uuid(),
    tenant_id uuid NOT NULL,
    phone_number_id uuid NOT NULL,
    about varchar(139),
    address varchar(256),
    description varchar(512),
    email varchar(128),
    websites text [],
    vertical varchar(50),
    profile_picture_url text,
    created_at timestamptz NOT NULL DEFAULT now(),
    updated_at timestamptz NOT NULL DEFAULT now(),
    created_by uuid,
    updated_by uuid,
    version integer NOT NULL DEFAULT 1,
    CONSTRAINT business_profiles_pkey PRIMARY KEY (id),
    CONSTRAINT business_profiles_tenant_id_fkey FOREIGN KEY (tenant_id)
    REFERENCES tenancy.tenants (id) ON DELETE RESTRICT,
    CONSTRAINT business_profiles_phone_number_id_fkey FOREIGN KEY (phone_number_id)
    REFERENCES waba.phone_numbers (id) ON DELETE CASCADE,
    CONSTRAINT business_profiles_phone_number_id_key UNIQUE (phone_number_id)
);

CREATE INDEX business_profiles_tenant_id_idx ON waba.business_profiles (tenant_id);

------------------------------------------------------------------------------
-- Row-Level Security (strict pattern: tenant_id is NOT NULL on all five tables)
------------------------------------------------------------------------------
ALTER TABLE waba.business_accounts ENABLE ROW LEVEL SECURITY;
ALTER TABLE waba.business_accounts FORCE ROW LEVEL SECURITY;

CREATE POLICY tenant_isolation ON waba.business_accounts
USING (tenant_id = app.current_tenant_id() OR app.is_platform_admin())
WITH CHECK (tenant_id = app.current_tenant_id() OR app.is_platform_admin());

ALTER TABLE waba.phone_numbers ENABLE ROW LEVEL SECURITY;
ALTER TABLE waba.phone_numbers FORCE ROW LEVEL SECURITY;

CREATE POLICY tenant_isolation ON waba.phone_numbers
USING (tenant_id = app.current_tenant_id() OR app.is_platform_admin())
WITH CHECK (tenant_id = app.current_tenant_id() OR app.is_platform_admin());

ALTER TABLE waba.phone_number_events ENABLE ROW LEVEL SECURITY;
ALTER TABLE waba.phone_number_events FORCE ROW LEVEL SECURITY;

CREATE POLICY tenant_isolation ON waba.phone_number_events
USING (tenant_id = app.current_tenant_id() OR app.is_platform_admin())
WITH CHECK (tenant_id = app.current_tenant_id() OR app.is_platform_admin());

ALTER TABLE waba.currency_migrations ENABLE ROW LEVEL SECURITY;
ALTER TABLE waba.currency_migrations FORCE ROW LEVEL SECURITY;

CREATE POLICY tenant_isolation ON waba.currency_migrations
USING (tenant_id = app.current_tenant_id() OR app.is_platform_admin())
WITH CHECK (tenant_id = app.current_tenant_id() OR app.is_platform_admin());

ALTER TABLE waba.business_profiles ENABLE ROW LEVEL SECURITY;
ALTER TABLE waba.business_profiles FORCE ROW LEVEL SECURITY;

CREATE POLICY tenant_isolation ON waba.business_profiles
USING (tenant_id = app.current_tenant_id() OR app.is_platform_admin())
WITH CHECK (tenant_id = app.current_tenant_id() OR app.is_platform_admin());

------------------------------------------------------------------------------
-- Grants
------------------------------------------------------------------------------
GRANT SELECT, INSERT, UPDATE, DELETE ON ALL TABLES IN SCHEMA waba TO app_user;
GRANT SELECT, INSERT, UPDATE, DELETE ON ALL TABLES IN SCHEMA waba TO platform_admin;

INSERT INTO public.schema_migrations (version)
VALUES ('V002')
ON CONFLICT (version) DO NOTHING;
