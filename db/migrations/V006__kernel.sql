-- V006__kernel.sql
-- Schema: kernel — core-identity infrastructure (wavio.SharedDataModel):
-- outbox_events, outbox_consumed_events, system_settings, file_attachments.
-- DDL derived 1:1 from Persistence/Configurations/Kernel/*.cs.
--
-- kernel.feature_flags is intentionally NOT created: platform feature flags
-- are canonical in system.feature_flags (V004) and core identity's FeatureFlag
-- entity is remapped there (orchestrator ruling — see db/README.md handoff).
--
-- RLS decisions:
--   outbox_events / outbox_consumed_events: NO RLS. The outbox dispatcher and
--     consumers are background workers with no tenant context; RLS would hide
--     every pending event from them. tenant_id is payload metadata here.
--   system_settings / file_attachments: nullable-tenant RLS pattern
--     (tenant_id IS NULL = platform-global row, visible to all sessions).

CREATE SCHEMA kernel;

GRANT USAGE ON SCHEMA kernel TO app_user;
GRANT USAGE ON SCHEMA kernel TO platform_admin;

------------------------------------------------------------------------------
-- outbox_events (transactional outbox; aggregate_id is polymorphic per
-- aggregate_type — documented FK-audit exclusion; correlation/causation ids
-- are trace identifiers, not references)
------------------------------------------------------------------------------
CREATE TABLE kernel.outbox_events (
    id uuid NOT NULL DEFAULT gen_random_uuid(),
    tenant_id uuid,
    aggregate_type varchar(100) NOT NULL,
    aggregate_id uuid NOT NULL,
    event_type varchar(100) NOT NULL,
    event_version smallint NOT NULL DEFAULT 1,
    payload jsonb NOT NULL,
    metadata jsonb NOT NULL DEFAULT '{}',
    correlation_id uuid,
    causation_id uuid,
    occurred_at timestamptz NOT NULL DEFAULT now(),
    published_at timestamptz,
    publish_attempts smallint NOT NULL DEFAULT 0,
    next_attempt_at timestamptz,
    last_error text,
    status varchar(20) NOT NULL DEFAULT 'pending',
    routing_key varchar(200),
    target_exchange varchar(100),
    idempotency_key varchar(100),
    created_at timestamptz NOT NULL DEFAULT now(),
    created_by uuid,
    CONSTRAINT outbox_events_pkey PRIMARY KEY (id),
    CONSTRAINT outbox_events_tenant_id_fkey FOREIGN KEY (tenant_id)
    REFERENCES tenancy.tenants (id) ON DELETE RESTRICT
);

CREATE UNIQUE INDEX outbox_events_idempotency_key_key
ON kernel.outbox_events (idempotency_key);

-- Dispatcher scan: unpublished events, oldest first.
CREATE INDEX outbox_events_dispatch_idx
ON kernel.outbox_events (occurred_at)
WHERE published_at IS NULL;

------------------------------------------------------------------------------
-- outbox_consumed_events (idempotent consumption marker per consumer;
-- event_id may originate from ANOTHER service's outbox arriving via RabbitMQ,
-- so no FK to kernel.outbox_events — documented FK-audit exclusion)
------------------------------------------------------------------------------
CREATE TABLE kernel.outbox_consumed_events (
    consumer_name varchar(100) NOT NULL,
    event_id uuid NOT NULL,
    processed_at timestamptz NOT NULL DEFAULT now(),
    CONSTRAINT outbox_consumed_events_pkey PRIMARY KEY (consumer_name, event_id)
);

------------------------------------------------------------------------------
-- system_settings (scope_type + nullable tenant_id; global rows have
-- tenant_id NULL and are readable by every session)
------------------------------------------------------------------------------
CREATE TABLE kernel.system_settings (
    id uuid NOT NULL DEFAULT gen_random_uuid(),
    tenant_id uuid,
    scope_type varchar(20) NOT NULL,
    category varchar(50) NOT NULL,
    setting_key varchar(100) NOT NULL,
    setting_value jsonb NOT NULL,
    data_type varchar(20) NOT NULL,
    description text,
    is_encrypted boolean NOT NULL DEFAULT FALSE,
    is_readonly boolean NOT NULL DEFAULT FALSE,
    requires_restart boolean NOT NULL DEFAULT FALSE,
    validation_schema jsonb,
    default_value jsonb,
    created_at timestamptz NOT NULL DEFAULT now(),
    updated_at timestamptz NOT NULL DEFAULT now(),
    updated_by uuid,
    version integer NOT NULL DEFAULT 1,
    created_by uuid,
    status varchar(20) NOT NULL DEFAULT 'active',
    CONSTRAINT system_settings_pkey PRIMARY KEY (id),
    CONSTRAINT system_settings_tenant_id_fkey FOREIGN KEY (tenant_id)
    REFERENCES tenancy.tenants (id) ON DELETE CASCADE
);

CREATE UNIQUE INDEX system_settings_scope_type_tenant_id_category_setting_key_key
ON kernel.system_settings (
    scope_type, tenant_id, category, setting_key
) NULLS NOT DISTINCT;

------------------------------------------------------------------------------
-- file_attachments (owner_id / uploaded_by_id are polymorphic per
-- owner_type / uploaded_by_type — documented FK-audit exclusions;
-- kms_key_id is a key alias string, not a reference)
------------------------------------------------------------------------------
CREATE TABLE kernel.file_attachments (
    id uuid NOT NULL DEFAULT gen_random_uuid(),
    tenant_id uuid,
    owner_type varchar(50) NOT NULL,
    owner_id uuid NOT NULL,
    purpose varchar(50) NOT NULL,
    s3_bucket varchar(100),
    s3_key text NOT NULL,
    storage_provider varchar(20) NOT NULL DEFAULT 's3',
    cdn_url text,
    thumbnail_s3_key text,
    file_name varchar(500) NOT NULL,
    mime_type varchar(100) NOT NULL,
    bytes bigint NOT NULL,
    sha256 character(64),
    width_px integer,
    height_px integer,
    duration_seconds integer,
    page_count smallint,
    is_public boolean NOT NULL DEFAULT FALSE,
    is_encrypted boolean NOT NULL DEFAULT FALSE,
    kms_key_id varchar(200),
    virus_scanned_at timestamptz,
    virus_scan_result varchar(20),
    expires_at timestamptz,
    uploaded_by_type varchar(20),
    uploaded_by_id uuid,
    uploaded_at timestamptz NOT NULL DEFAULT now(),
    last_accessed_at timestamptz,
    access_count integer NOT NULL DEFAULT 0,
    metadata jsonb NOT NULL DEFAULT '{}',
    deleted_at timestamptz,
    created_at timestamptz NOT NULL DEFAULT now(),
    created_by uuid,
    CONSTRAINT file_attachments_pkey PRIMARY KEY (id),
    CONSTRAINT file_attachments_tenant_id_fkey FOREIGN KEY (tenant_id)
    REFERENCES tenancy.tenants (id) ON DELETE CASCADE
);

CREATE INDEX file_attachments_tenant_id_idx
ON kernel.file_attachments (tenant_id);
CREATE INDEX file_attachments_owner_type_owner_id_idx
ON kernel.file_attachments (owner_type, owner_id);

------------------------------------------------------------------------------
-- Row-Level Security
------------------------------------------------------------------------------
ALTER TABLE kernel.system_settings ENABLE ROW LEVEL SECURITY;
ALTER TABLE kernel.system_settings FORCE ROW LEVEL SECURITY;

CREATE POLICY tenant_isolation ON kernel.system_settings
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

ALTER TABLE kernel.file_attachments ENABLE ROW LEVEL SECURITY;
ALTER TABLE kernel.file_attachments FORCE ROW LEVEL SECURITY;

CREATE POLICY tenant_isolation ON kernel.file_attachments
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
-- Grants
------------------------------------------------------------------------------
GRANT SELECT, INSERT, UPDATE, DELETE ON ALL TABLES IN SCHEMA kernel TO app_user;
GRANT SELECT, INSERT, UPDATE, DELETE ON ALL TABLES IN SCHEMA kernel TO platform_admin;

INSERT INTO public.schema_migrations (version)
VALUES ('V006')
ON CONFLICT (version) DO NOTHING;
