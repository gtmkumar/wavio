-- V012__consent.sql
-- Schema: consent — opt_in_events, opt_out_events, erasure_requests,
-- retention_policies (spec §6, §4.10, §9; issue #21, #23).
--
-- DPDP Act 2023 compliance layer. opt_in_events / opt_out_events are
-- APPEND-ONLY evidence ledgers (created_* only, no updates by convention).
-- Opt-out (STOP listener) feeds the existing messaging.suppression_list which
-- the gateway enforces deny-wins pre-dispatch — that table is NOT redefined
-- here (it lives in the messaging schema, V007). Erasure removes message
-- content but PRESERVES cost-ledger metadata (8-year tax retention) — the
-- workflow state lives in erasure_requests.
--
-- RLS: opt_in_events, opt_out_events, erasure_requests are tenant-scoped
-- (strict pattern). retention_policies uses the nullable-tenant pattern —
-- a NULL tenant_id row is the platform default policy visible to every tenant
-- (same posture as system.feature_flags / templates.template_packs).

CREATE SCHEMA consent;

GRANT USAGE ON SCHEMA consent TO app_user;
GRANT USAGE ON SCHEMA consent TO platform_admin;

------------------------------------------------------------------------------
-- opt_in_events: append-only opt-in evidence per (tenant, wa_id, purpose)
-- (spec §4.10). `actor` is free-text (who captured it) — not a platform user
-- FK; created_by captures the platform actor when there is one.
------------------------------------------------------------------------------
CREATE TABLE consent.opt_in_events (
    id uuid NOT NULL DEFAULT gen_random_uuid(),
    tenant_id uuid NOT NULL,
    wa_id varchar(20) NOT NULL,
    purpose varchar(20) NOT NULL,
    capture_channel varchar(20) NOT NULL,
    evidence jsonb,
    evidence_wamid varchar(128),
    actor varchar(120),
    source_ip inet,
    occurred_at timestamptz NOT NULL DEFAULT now(),
    created_at timestamptz NOT NULL DEFAULT now(),
    created_by uuid,
    CONSTRAINT opt_in_events_pkey PRIMARY KEY (id),
    CONSTRAINT opt_in_events_tenant_id_fkey FOREIGN KEY (tenant_id)
    REFERENCES tenancy.tenants (id) ON DELETE RESTRICT,
    CONSTRAINT opt_in_events_purpose_check CHECK (
        purpose IN ('transactional', 'marketing', 'service')
    ),
    CONSTRAINT opt_in_events_capture_channel_check CHECK (
        capture_channel IN (
            'web_form', 'qr', 'in_chat', 'in_person', 'api', 'import'
        )
    )
);

CREATE INDEX opt_in_events_tenant_wa_id_occurred_at_idx
ON consent.opt_in_events (tenant_id, wa_id, occurred_at DESC);

------------------------------------------------------------------------------
-- opt_out_events: append-only opt-out ledger (spec §4.10). The STOP-keyword
-- listener writes one row here, then the app suppresses the (tenant, wa_id)
-- in messaging.suppression_list. inbound_wamid links the triggering message.
------------------------------------------------------------------------------
CREATE TABLE consent.opt_out_events (
    id uuid NOT NULL DEFAULT gen_random_uuid(),
    tenant_id uuid NOT NULL,
    wa_id varchar(20) NOT NULL,
    scope varchar(20) NOT NULL DEFAULT 'marketing',
    reason varchar(30) NOT NULL DEFAULT 'stop_keyword',
    keyword varchar(40),
    language varchar(15),
    inbound_wamid varchar(128),
    payload jsonb,
    occurred_at timestamptz NOT NULL DEFAULT now(),
    created_at timestamptz NOT NULL DEFAULT now(),
    created_by uuid,
    CONSTRAINT opt_out_events_pkey PRIMARY KEY (id),
    CONSTRAINT opt_out_events_tenant_id_fkey FOREIGN KEY (tenant_id)
    REFERENCES tenancy.tenants (id) ON DELETE RESTRICT,
    CONSTRAINT opt_out_events_scope_check CHECK (
        scope IN ('marketing', 'all')
    ),
    CONSTRAINT opt_out_events_reason_check CHECK (
        reason IN ('stop_keyword', 'manual', 'complaint', 'bounce')
    )
);

CREATE INDEX opt_out_events_tenant_wa_id_occurred_at_idx
ON consent.opt_out_events (tenant_id, wa_id, occurred_at DESC);

------------------------------------------------------------------------------
-- erasure_requests: DPDP data-principal rights workflow per (tenant, wa_id)
-- (spec §4.10, §9). content_erased_at marks the point content was removed;
-- cost-ledger metadata is deliberately preserved for tax retention.
------------------------------------------------------------------------------
CREATE TABLE consent.erasure_requests (
    id uuid NOT NULL DEFAULT gen_random_uuid(),
    tenant_id uuid NOT NULL,
    wa_id varchar(20) NOT NULL,
    request_type varchar(20) NOT NULL DEFAULT 'erasure',
    status varchar(20) NOT NULL DEFAULT 'pending',
    requested_by varchar(120),
    reason text,
    scope jsonb,
    content_erased_at timestamptz,
    export_ref varchar(256),
    completed_at timestamptz,
    created_at timestamptz NOT NULL DEFAULT now(),
    updated_at timestamptz NOT NULL DEFAULT now(),
    created_by uuid,
    updated_by uuid,
    version integer NOT NULL DEFAULT 1,
    CONSTRAINT erasure_requests_pkey PRIMARY KEY (id),
    CONSTRAINT erasure_requests_tenant_id_fkey FOREIGN KEY (tenant_id)
    REFERENCES tenancy.tenants (id) ON DELETE RESTRICT,
    CONSTRAINT erasure_requests_request_type_check CHECK (
        request_type IN ('erasure', 'export')
    ),
    CONSTRAINT erasure_requests_status_check CHECK (
        status IN ('pending', 'processing', 'completed', 'rejected')
    )
);

CREATE INDEX erasure_requests_tenant_wa_id_idx
ON consent.erasure_requests (tenant_id, wa_id);
-- Worker hot path: outstanding requests to process.
CREATE INDEX erasure_requests_status_idx
ON consent.erasure_requests (status)
WHERE status IN ('pending', 'processing');

------------------------------------------------------------------------------
-- retention_policies: configurable retention per data class (spec §4.10 —
-- default message content 12 months, metadata/cost ledger 8 years for tax).
-- Nullable-tenant: NULL = platform default, non-NULL = tenant override.
------------------------------------------------------------------------------
CREATE TABLE consent.retention_policies (
    id uuid NOT NULL DEFAULT gen_random_uuid(),
    tenant_id uuid,
    data_class varchar(30) NOT NULL,
    retention_days integer NOT NULL,
    basis varchar(30),
    enabled boolean NOT NULL DEFAULT TRUE,
    created_at timestamptz NOT NULL DEFAULT now(),
    updated_at timestamptz NOT NULL DEFAULT now(),
    created_by uuid,
    updated_by uuid,
    version integer NOT NULL DEFAULT 1,
    CONSTRAINT retention_policies_pkey PRIMARY KEY (id),
    CONSTRAINT retention_policies_tenant_id_fkey FOREIGN KEY (tenant_id)
    REFERENCES tenancy.tenants (id) ON DELETE CASCADE,
    CONSTRAINT retention_policies_data_class_check CHECK (
        data_class IN (
            'message_content', 'metadata', 'cost_ledger',
            'consent_evidence', 'raw_webhook'
        )
    ),
    CONSTRAINT retention_policies_retention_days_check CHECK (
        retention_days > 0
    )
);

-- NULLS NOT DISTINCT: at most one platform-default policy per data class.
CREATE UNIQUE INDEX retention_policies_tenant_id_data_class_key
ON consent.retention_policies (tenant_id, data_class) NULLS NOT DISTINCT;

------------------------------------------------------------------------------
-- Row-Level Security
------------------------------------------------------------------------------
ALTER TABLE consent.opt_in_events ENABLE ROW LEVEL SECURITY;
ALTER TABLE consent.opt_in_events FORCE ROW LEVEL SECURITY;

CREATE POLICY tenant_isolation ON consent.opt_in_events
USING (tenant_id = app.current_tenant_id() OR app.is_platform_admin())
WITH CHECK (tenant_id = app.current_tenant_id() OR app.is_platform_admin());

ALTER TABLE consent.opt_out_events ENABLE ROW LEVEL SECURITY;
ALTER TABLE consent.opt_out_events FORCE ROW LEVEL SECURITY;

CREATE POLICY tenant_isolation ON consent.opt_out_events
USING (tenant_id = app.current_tenant_id() OR app.is_platform_admin())
WITH CHECK (tenant_id = app.current_tenant_id() OR app.is_platform_admin());

ALTER TABLE consent.erasure_requests ENABLE ROW LEVEL SECURITY;
ALTER TABLE consent.erasure_requests FORCE ROW LEVEL SECURITY;

CREATE POLICY tenant_isolation ON consent.erasure_requests
USING (tenant_id = app.current_tenant_id() OR app.is_platform_admin())
WITH CHECK (tenant_id = app.current_tenant_id() OR app.is_platform_admin());

ALTER TABLE consent.retention_policies ENABLE ROW LEVEL SECURITY;
ALTER TABLE consent.retention_policies FORCE ROW LEVEL SECURITY;

-- Nullable-tenant pattern: NULL = platform default, visible to all tenants.
CREATE POLICY tenant_isolation ON consent.retention_policies
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
GRANT SELECT, INSERT, UPDATE, DELETE ON ALL TABLES IN SCHEMA consent TO app_user;
GRANT SELECT, INSERT, UPDATE, DELETE ON ALL TABLES IN SCHEMA consent
TO platform_admin;

INSERT INTO public.schema_migrations (version)
VALUES ('V012')
ON CONFLICT (version) DO NOTHING;
