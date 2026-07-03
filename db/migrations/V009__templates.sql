-- V009__templates.sql
-- Schema: templates — templates, template_versions, template_status_events,
-- template_category_changes, template_lint_results, template_packs
-- (spec §6, §4.4; issue #16).
--
-- Status state machine (issue #16), CHECK-enforced values, transitions
-- app-enforced and recorded in template_status_events:
--   DRAFT -> PENDING (submitted to Meta)
--   PENDING -> APPROVED | REJECTED
--   APPROVED -> PAUSED (Meta auto-pause 3h -> 6h) | DISABLED
--   PAUSED -> APPROVED (unpaused) | DISABLED (third strike)
--   REJECTED -> DRAFT (edit creates a new version, back to draft)
--   DISABLED -> (terminal)
-- Versioning: templates are immutable post-approval — edits create a new
-- template_versions row; campaigns pin template_version_id (spec §4.4).
--
-- Also closes the V007 deferred FKs: messaging.outbound_messages.template_id
-- / template_version_id now get their constraints (templates schema exists).
--
-- RLS: all tables strict-pattern EXCEPT template_packs, which uses the
-- nullable-tenant pattern — packs with tenant_id NULL are the platform's
-- pre-approved vertical library (spec §4.4), visible to every tenant.

CREATE SCHEMA templates;

GRANT USAGE ON SCHEMA templates TO app_user;
GRANT USAGE ON SCHEMA templates TO platform_admin;

------------------------------------------------------------------------------
-- templates: one logical template per (WABA, name, language).
------------------------------------------------------------------------------
CREATE TABLE templates.templates (
    id uuid NOT NULL DEFAULT gen_random_uuid(),
    tenant_id uuid NOT NULL,
    business_account_id uuid NOT NULL,
    name varchar(512) NOT NULL,
    language varchar(15) NOT NULL,
    category varchar(20) NOT NULL,
    meta_template_id varchar(64),
    status varchar(20) NOT NULL DEFAULT 'DRAFT',
    current_version_id uuid,
    paused_until timestamptz,
    pause_count smallint NOT NULL DEFAULT 0,
    created_at timestamptz NOT NULL DEFAULT now(),
    updated_at timestamptz NOT NULL DEFAULT now(),
    created_by uuid,
    updated_by uuid,
    version integer NOT NULL DEFAULT 1,
    deleted_at timestamptz,
    CONSTRAINT templates_pkey PRIMARY KEY (id),
    CONSTRAINT templates_tenant_id_fkey FOREIGN KEY (tenant_id)
    REFERENCES tenancy.tenants (id) ON DELETE RESTRICT,
    CONSTRAINT templates_business_account_id_fkey
    FOREIGN KEY (business_account_id)
    REFERENCES waba.business_accounts (id) ON DELETE CASCADE,
    CONSTRAINT templates_business_account_id_name_language_key
    UNIQUE (business_account_id, name, language),
    CONSTRAINT templates_category_check CHECK (
        category IN ('marketing', 'utility', 'authentication')
    ),
    CONSTRAINT templates_status_check CHECK (
        status IN (
            'DRAFT', 'PENDING', 'APPROVED', 'REJECTED', 'PAUSED', 'DISABLED'
        )
    )
);

CREATE INDEX templates_tenant_id_idx ON templates.templates (tenant_id);
CREATE INDEX templates_business_account_id_idx
ON templates.templates (business_account_id);
CREATE INDEX templates_meta_template_id_idx
ON templates.templates (meta_template_id);

COMMENT ON COLUMN templates.templates.pause_count IS
'Meta auto-pause escalation: 1st pause 3h, 2nd 6h, 3rd -> DISABLED
(spec §4.4). Guardian freezes campaigns using a paused template.';

------------------------------------------------------------------------------
-- template_versions: immutable post-approval content snapshots.
------------------------------------------------------------------------------
CREATE TABLE templates.template_versions (
    id uuid NOT NULL DEFAULT gen_random_uuid(),
    tenant_id uuid NOT NULL,
    template_id uuid NOT NULL,
    version_number integer NOT NULL,
    components jsonb NOT NULL,
    example_values jsonb,
    status varchar(20) NOT NULL DEFAULT 'DRAFT',
    rejection_reason text,
    submitted_at timestamptz,
    reviewed_at timestamptz,
    created_at timestamptz NOT NULL DEFAULT now(),
    updated_at timestamptz NOT NULL DEFAULT now(),
    created_by uuid,
    updated_by uuid,
    CONSTRAINT template_versions_pkey PRIMARY KEY (id),
    CONSTRAINT template_versions_tenant_id_fkey FOREIGN KEY (tenant_id)
    REFERENCES tenancy.tenants (id) ON DELETE RESTRICT,
    CONSTRAINT template_versions_template_id_fkey FOREIGN KEY (template_id)
    REFERENCES templates.templates (id) ON DELETE CASCADE,
    CONSTRAINT template_versions_template_id_version_number_key
    UNIQUE (template_id, version_number),
    CONSTRAINT template_versions_status_check CHECK (
        status IN (
            'DRAFT', 'PENDING', 'APPROVED', 'REJECTED', 'PAUSED', 'DISABLED'
        )
    )
);

CREATE INDEX template_versions_tenant_id_idx
ON templates.template_versions (tenant_id);

-- Circular pair with templates.current_version_id — added after both exist.
ALTER TABLE templates.templates
ADD CONSTRAINT templates_current_version_id_fkey
FOREIGN KEY (current_version_id)
REFERENCES templates.template_versions (id) ON DELETE SET NULL;

------------------------------------------------------------------------------
-- template_status_events: append-only state-machine transition log
-- (webhook `wa.template.status_changed` + local transitions).
------------------------------------------------------------------------------
CREATE TABLE templates.template_status_events (
    id uuid NOT NULL DEFAULT gen_random_uuid(),
    tenant_id uuid NOT NULL,
    template_id uuid NOT NULL,
    template_version_id uuid,
    old_status varchar(20),
    new_status varchar(20) NOT NULL,
    reason text,
    payload jsonb,
    occurred_at timestamptz NOT NULL DEFAULT now(),
    created_at timestamptz NOT NULL DEFAULT now(),
    created_by uuid,
    CONSTRAINT template_status_events_pkey PRIMARY KEY (id),
    CONSTRAINT template_status_events_tenant_id_fkey FOREIGN KEY (tenant_id)
    REFERENCES tenancy.tenants (id) ON DELETE RESTRICT,
    CONSTRAINT template_status_events_template_id_fkey
    FOREIGN KEY (template_id)
    REFERENCES templates.templates (id) ON DELETE CASCADE,
    CONSTRAINT template_status_events_template_version_id_fkey
    FOREIGN KEY (template_version_id)
    REFERENCES templates.template_versions (id) ON DELETE SET NULL
);

CREATE INDEX template_status_events_template_id_occurred_at_idx
ON templates.template_status_events (template_id, occurred_at DESC);
CREATE INDEX template_status_events_tenant_id_idx
ON templates.template_status_events (tenant_id);

------------------------------------------------------------------------------
-- template_category_changes: Meta reclassification (utility -> marketing
-- changes cost — tenant alert + billing recalibration are MANDATORY hooks,
-- tracked by the two *_at columns; spec §4.4, issue #16).
------------------------------------------------------------------------------
CREATE TABLE templates.template_category_changes (
    id uuid NOT NULL DEFAULT gen_random_uuid(),
    tenant_id uuid NOT NULL,
    template_id uuid NOT NULL,
    old_category varchar(20) NOT NULL,
    new_category varchar(20) NOT NULL,
    occurred_at timestamptz NOT NULL DEFAULT now(),
    tenant_alerted_at timestamptz,
    billing_recalibrated_at timestamptz,
    payload jsonb,
    created_at timestamptz NOT NULL DEFAULT now(),
    updated_at timestamptz NOT NULL DEFAULT now(),
    created_by uuid,
    updated_by uuid,
    CONSTRAINT template_category_changes_pkey PRIMARY KEY (id),
    CONSTRAINT template_category_changes_tenant_id_fkey FOREIGN KEY (tenant_id)
    REFERENCES tenancy.tenants (id) ON DELETE RESTRICT,
    CONSTRAINT template_category_changes_template_id_fkey
    FOREIGN KEY (template_id)
    REFERENCES templates.templates (id) ON DELETE CASCADE
);

CREATE INDEX template_category_changes_template_id_idx
ON templates.template_category_changes (template_id);
CREATE INDEX template_category_changes_tenant_id_idx
ON templates.template_category_changes (tenant_id);

------------------------------------------------------------------------------
-- template_lint_results: per-version lint runs. Wave 1 ships the always-pass
-- 'stub' linter (issue #16); Wave 3 adds 'rules' and 'llm'.
------------------------------------------------------------------------------
CREATE TABLE templates.template_lint_results (
    id uuid NOT NULL DEFAULT gen_random_uuid(),
    tenant_id uuid NOT NULL,
    template_version_id uuid NOT NULL,
    linter varchar(30) NOT NULL DEFAULT 'stub',
    passed boolean NOT NULL,
    findings jsonb NOT NULL DEFAULT '[]',
    score smallint,
    created_at timestamptz NOT NULL DEFAULT now(),
    created_by uuid,
    CONSTRAINT template_lint_results_pkey PRIMARY KEY (id),
    CONSTRAINT template_lint_results_tenant_id_fkey FOREIGN KEY (tenant_id)
    REFERENCES tenancy.tenants (id) ON DELETE RESTRICT,
    CONSTRAINT template_lint_results_template_version_id_fkey
    FOREIGN KEY (template_version_id)
    REFERENCES templates.template_versions (id) ON DELETE CASCADE,
    CONSTRAINT template_lint_results_linter_check CHECK (
        linter IN ('stub', 'rules', 'llm')
    )
);

CREATE INDEX template_lint_results_template_version_id_idx
ON templates.template_lint_results (template_version_id);
CREATE INDEX template_lint_results_tenant_id_idx
ON templates.template_lint_results (tenant_id);

------------------------------------------------------------------------------
-- template_packs: pre-approved vertical template libraries (spec §4.4).
-- tenant_id NULL = platform-shipped pack (global, readable by every tenant);
-- non-NULL = a tenant's private pack. Pack contents are definition blueprints
-- (jsonb), instantiated into templates.templates per tenant on adoption.
------------------------------------------------------------------------------
CREATE TABLE templates.template_packs (
    id uuid NOT NULL DEFAULT gen_random_uuid(),
    tenant_id uuid,
    pack_key varchar(100) NOT NULL,
    vertical varchar(50) NOT NULL,
    name varchar(200) NOT NULL,
    description text,
    definitions jsonb NOT NULL DEFAULT '[]',
    status varchar(20) NOT NULL DEFAULT 'active',
    created_at timestamptz NOT NULL DEFAULT now(),
    updated_at timestamptz NOT NULL DEFAULT now(),
    created_by uuid,
    updated_by uuid,
    version integer NOT NULL DEFAULT 1,
    CONSTRAINT template_packs_pkey PRIMARY KEY (id),
    CONSTRAINT template_packs_tenant_id_fkey FOREIGN KEY (tenant_id)
    REFERENCES tenancy.tenants (id) ON DELETE CASCADE
);

CREATE UNIQUE INDEX template_packs_tenant_id_pack_key_key
ON templates.template_packs (tenant_id, pack_key) NULLS NOT DISTINCT;

------------------------------------------------------------------------------
-- Deferred FKs from V007 (templates schema now exists). SET NULL: message
-- history must survive template cleanup; the sent content is retained in
-- outbound_messages.payload.
------------------------------------------------------------------------------
ALTER TABLE messaging.outbound_messages
ADD CONSTRAINT outbound_messages_template_id_fkey
FOREIGN KEY (template_id)
REFERENCES templates.templates (id) ON DELETE SET NULL;

ALTER TABLE messaging.outbound_messages
ADD CONSTRAINT outbound_messages_template_version_id_fkey
FOREIGN KEY (template_version_id)
REFERENCES templates.template_versions (id) ON DELETE SET NULL;

------------------------------------------------------------------------------
-- Row-Level Security
------------------------------------------------------------------------------
ALTER TABLE templates.templates ENABLE ROW LEVEL SECURITY;
ALTER TABLE templates.templates FORCE ROW LEVEL SECURITY;

CREATE POLICY tenant_isolation ON templates.templates
USING (tenant_id = app.current_tenant_id() OR app.is_platform_admin())
WITH CHECK (tenant_id = app.current_tenant_id() OR app.is_platform_admin());

ALTER TABLE templates.template_versions ENABLE ROW LEVEL SECURITY;
ALTER TABLE templates.template_versions FORCE ROW LEVEL SECURITY;

CREATE POLICY tenant_isolation ON templates.template_versions
USING (tenant_id = app.current_tenant_id() OR app.is_platform_admin())
WITH CHECK (tenant_id = app.current_tenant_id() OR app.is_platform_admin());

ALTER TABLE templates.template_status_events ENABLE ROW LEVEL SECURITY;
ALTER TABLE templates.template_status_events FORCE ROW LEVEL SECURITY;

CREATE POLICY tenant_isolation ON templates.template_status_events
USING (tenant_id = app.current_tenant_id() OR app.is_platform_admin())
WITH CHECK (tenant_id = app.current_tenant_id() OR app.is_platform_admin());

ALTER TABLE templates.template_category_changes ENABLE ROW LEVEL SECURITY;
ALTER TABLE templates.template_category_changes FORCE ROW LEVEL SECURITY;

CREATE POLICY tenant_isolation ON templates.template_category_changes
USING (tenant_id = app.current_tenant_id() OR app.is_platform_admin())
WITH CHECK (tenant_id = app.current_tenant_id() OR app.is_platform_admin());

ALTER TABLE templates.template_lint_results ENABLE ROW LEVEL SECURITY;
ALTER TABLE templates.template_lint_results FORCE ROW LEVEL SECURITY;

CREATE POLICY tenant_isolation ON templates.template_lint_results
USING (tenant_id = app.current_tenant_id() OR app.is_platform_admin())
WITH CHECK (tenant_id = app.current_tenant_id() OR app.is_platform_admin());

ALTER TABLE templates.template_packs ENABLE ROW LEVEL SECURITY;
ALTER TABLE templates.template_packs FORCE ROW LEVEL SECURITY;

-- Nullable-tenant pattern: NULL = platform pack, visible to all tenants.
CREATE POLICY tenant_isolation ON templates.template_packs
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
GRANT SELECT, INSERT, UPDATE, DELETE ON ALL TABLES IN SCHEMA templates TO app_user;
GRANT SELECT, INSERT, UPDATE, DELETE ON ALL TABLES IN SCHEMA templates
TO platform_admin;

INSERT INTO public.schema_migrations (version)
VALUES ('V009')
ON CONFLICT (version) DO NOTHING;
