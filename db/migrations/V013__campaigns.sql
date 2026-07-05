-- V013__campaigns.sql
-- Schema: messaging (extension) — campaigns, campaign_recipients
-- (spec §4.2 "campaign engine chunks broadcasts to fit tier headroom",
-- §7.1 POST /v1/campaigns; issue #22).
--
-- NOTE: the spec §6 schema map predates the campaign engine and lists no
-- campaign tables — these live in the existing messaging schema (wa-gateway
-- owns broadcast dispatch; campaigns fan out into messaging.outbound_messages
-- rows through the same accept path, so they share the schema and its RLS
-- posture). db/README.md's ownership map is updated alongside this file.
--
-- campaigns pin an immutable template version (spec §4.4: "templates immutable
-- post-approval; edits create new versions; campaigns pin versions").
-- campaign_recipients holds per-recipient fan-out state so a broadcast larger
-- than the current tier headroom resumes across days: the chunker repeatedly
-- claims 'pending' rows up to headroom; rollup counters on campaigns are
-- derived from recipient status transitions.
--
-- RLS: both tables tenant-scoped (strict pattern).

------------------------------------------------------------------------------
-- campaigns: one row per POST /v1/campaigns broadcast.
------------------------------------------------------------------------------
CREATE TABLE messaging.campaigns (
    id uuid NOT NULL DEFAULT gen_random_uuid(),
    tenant_id uuid NOT NULL,
    phone_number_id uuid NOT NULL,
    name varchar(200) NOT NULL,
    template_version_id uuid NOT NULL,
    params jsonb,
    status varchar(20) NOT NULL DEFAULT 'draft',
    scheduled_at timestamptz,
    started_at timestamptz,
    completed_at timestamptz,
    audience_count integer NOT NULL DEFAULT 0,
    suppressed_count integer NOT NULL DEFAULT 0,
    sent_count integer NOT NULL DEFAULT 0,
    delivered_count integer NOT NULL DEFAULT 0,
    read_count integer NOT NULL DEFAULT 0,
    failed_count integer NOT NULL DEFAULT 0,
    projected_cost numeric(12, 4),
    projected_currency varchar(3),
    created_at timestamptz NOT NULL DEFAULT now(),
    updated_at timestamptz NOT NULL DEFAULT now(),
    created_by uuid,
    updated_by uuid,
    version integer NOT NULL DEFAULT 1,
    CONSTRAINT campaigns_pkey PRIMARY KEY (id),
    CONSTRAINT campaigns_tenant_id_fkey FOREIGN KEY (tenant_id)
    REFERENCES tenancy.tenants (id) ON DELETE RESTRICT,
    CONSTRAINT campaigns_phone_number_id_fkey FOREIGN KEY (phone_number_id)
    REFERENCES waba.phone_numbers (id) ON DELETE RESTRICT,
    CONSTRAINT campaigns_template_version_id_fkey
    FOREIGN KEY (template_version_id)
    REFERENCES templates.template_versions (id) ON DELETE RESTRICT,
    CONSTRAINT campaigns_status_check CHECK (
        status IN (
            'draft', 'scheduled', 'running', 'paused',
            'completed', 'cancelled', 'failed'
        )
    )
);

CREATE INDEX campaigns_tenant_id_status_idx
ON messaging.campaigns (tenant_id, status);
CREATE INDEX campaigns_phone_number_id_idx
ON messaging.campaigns (phone_number_id);
CREATE INDEX campaigns_template_version_id_idx
ON messaging.campaigns (template_version_id);

COMMENT ON COLUMN messaging.campaigns.status IS
'draft -> scheduled -> running -> completed, with paused (Guardian throttle /
template pause / manual), cancelled (terminal, remaining recipients marked
cancelled) and failed (terminal, launch-level error). Counters are rollups
derived from campaign_recipients.';

------------------------------------------------------------------------------
-- campaign_recipients: per-recipient fan-out state (multi-day chunk resume).
-- Suppressed recipients are marked up-front at launch (spec §4.10 deny-wins);
-- the chunker only ever claims 'pending' rows.
------------------------------------------------------------------------------
CREATE TABLE messaging.campaign_recipients (
    id uuid NOT NULL DEFAULT gen_random_uuid(),
    tenant_id uuid NOT NULL,
    campaign_id uuid NOT NULL,
    wa_id varchar(20) NOT NULL,
    params jsonb,
    status varchar(20) NOT NULL DEFAULT 'pending',
    outbound_message_id uuid,
    error_code varchar(20),
    processed_at timestamptz,
    created_at timestamptz NOT NULL DEFAULT now(),
    updated_at timestamptz NOT NULL DEFAULT now(),
    created_by uuid,
    updated_by uuid,
    version integer NOT NULL DEFAULT 1,
    CONSTRAINT campaign_recipients_pkey PRIMARY KEY (id),
    CONSTRAINT campaign_recipients_tenant_id_fkey FOREIGN KEY (tenant_id)
    REFERENCES tenancy.tenants (id) ON DELETE RESTRICT,
    CONSTRAINT campaign_recipients_campaign_id_fkey FOREIGN KEY (campaign_id)
    REFERENCES messaging.campaigns (id) ON DELETE CASCADE,
    CONSTRAINT campaign_recipients_outbound_message_id_fkey
    FOREIGN KEY (outbound_message_id)
    REFERENCES messaging.outbound_messages (id) ON DELETE SET NULL,
    CONSTRAINT campaign_recipients_campaign_id_wa_id_key
    UNIQUE (campaign_id, wa_id),
    CONSTRAINT campaign_recipients_status_check CHECK (
        status IN (
            'pending', 'suppressed', 'sent', 'delivered',
            'read', 'failed', 'cancelled'
        )
    )
);

-- Chunker hot path: claim the next pending recipients for a campaign.
CREATE INDEX campaign_recipients_campaign_pending_idx
ON messaging.campaign_recipients (campaign_id)
WHERE status = 'pending';
CREATE INDEX campaign_recipients_tenant_id_idx
ON messaging.campaign_recipients (tenant_id);
CREATE INDEX campaign_recipients_outbound_message_id_idx
ON messaging.campaign_recipients (outbound_message_id);

COMMENT ON COLUMN messaging.campaign_recipients.status IS
'pending -> sent (outbound accepted; outbound_message_id set) -> delivered ->
read, or suppressed (marked at launch, never dispatched), failed (dispatch or
delivery failure; error_code set), cancelled (campaign cancelled before
dispatch). Delivery-state transitions mirror the linked outbound_message.';

------------------------------------------------------------------------------
-- Row-Level Security (strict pattern)
------------------------------------------------------------------------------
ALTER TABLE messaging.campaigns ENABLE ROW LEVEL SECURITY;
ALTER TABLE messaging.campaigns FORCE ROW LEVEL SECURITY;

CREATE POLICY tenant_isolation ON messaging.campaigns
USING (tenant_id = app.current_tenant_id() OR app.is_platform_admin())
WITH CHECK (tenant_id = app.current_tenant_id() OR app.is_platform_admin());

ALTER TABLE messaging.campaign_recipients ENABLE ROW LEVEL SECURITY;
ALTER TABLE messaging.campaign_recipients FORCE ROW LEVEL SECURITY;

CREATE POLICY tenant_isolation ON messaging.campaign_recipients
USING (tenant_id = app.current_tenant_id() OR app.is_platform_admin())
WITH CHECK (tenant_id = app.current_tenant_id() OR app.is_platform_admin());

------------------------------------------------------------------------------
-- Grants (schema usage already granted in V007)
------------------------------------------------------------------------------
GRANT SELECT, INSERT, UPDATE, DELETE ON messaging.campaigns TO app_user;
GRANT SELECT, INSERT, UPDATE, DELETE ON messaging.campaigns TO platform_admin;
GRANT SELECT, INSERT, UPDATE, DELETE
ON messaging.campaign_recipients TO app_user;
GRANT SELECT, INSERT, UPDATE, DELETE
ON messaging.campaign_recipients TO platform_admin;

INSERT INTO public.schema_migrations (version)
VALUES ('V013')
ON CONFLICT (version) DO NOTHING;
