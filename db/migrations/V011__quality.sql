-- V011__quality.sql
-- Schema: quality — number_quality_events, messaging_tier_events,
-- guardian_incidents, health_snapshots (spec §6, §4.6; issue #20, #23).
--
-- Quality Rating Guardian: ingest phone-number quality (GREEN/YELLOW/RED) and
-- messaging-tier changes, open incidents that drive auto-throttle in the
-- gateway (YELLOW -> marketing 50%; RED -> marketing frozen), and roll up a
-- weekly per-number health snapshot (spec §4.6).
--
-- RLS: all tables tenant-scoped (strict pattern). The *_events tables are
-- append-only history (created_* only, no version), matching sessions.window_
-- events / templates.template_status_events.

CREATE SCHEMA quality;

GRANT USAGE ON SCHEMA quality TO app_user;
GRANT USAGE ON SCHEMA quality TO platform_admin;

------------------------------------------------------------------------------
-- number_quality_events: append-only log of `phone_number_quality_update`
-- webhooks (GREEN/YELLOW/RED) per number (spec §4.6).
------------------------------------------------------------------------------
CREATE TABLE quality.number_quality_events (
    id uuid NOT NULL DEFAULT gen_random_uuid(),
    tenant_id uuid NOT NULL,
    phone_number_id uuid NOT NULL,
    old_rating varchar(10),
    new_rating varchar(10) NOT NULL,
    event_source varchar(20) NOT NULL DEFAULT 'webhook',
    payload jsonb,
    occurred_at timestamptz NOT NULL DEFAULT now(),
    created_at timestamptz NOT NULL DEFAULT now(),
    created_by uuid,
    CONSTRAINT number_quality_events_pkey PRIMARY KEY (id),
    CONSTRAINT number_quality_events_tenant_id_fkey FOREIGN KEY (tenant_id)
    REFERENCES tenancy.tenants (id) ON DELETE RESTRICT,
    CONSTRAINT number_quality_events_phone_number_id_fkey
    FOREIGN KEY (phone_number_id)
    REFERENCES waba.phone_numbers (id) ON DELETE CASCADE,
    CONSTRAINT number_quality_events_new_rating_check CHECK (
        new_rating IN ('green', 'yellow', 'red', 'unknown')
    ),
    CONSTRAINT number_quality_events_old_rating_check CHECK (
        old_rating IN ('green', 'yellow', 'red', 'unknown')
    ),
    CONSTRAINT number_quality_events_event_source_check CHECK (
        event_source IN ('webhook', 'manual', 'simulated')
    )
);

CREATE INDEX number_quality_events_number_occurred_at_idx
ON quality.number_quality_events (phone_number_id, occurred_at DESC);
CREATE INDEX number_quality_events_tenant_id_idx
ON quality.number_quality_events (tenant_id);

------------------------------------------------------------------------------
-- messaging_tier_events: append-only log of messaging-tier changes per number
-- (250 -> 1K -> 10K -> 100K -> unlimited; spec §4.6).
------------------------------------------------------------------------------
CREATE TABLE quality.messaging_tier_events (
    id uuid NOT NULL DEFAULT gen_random_uuid(),
    tenant_id uuid NOT NULL,
    phone_number_id uuid NOT NULL,
    old_tier varchar(20),
    new_tier varchar(20) NOT NULL,
    event_source varchar(20) NOT NULL DEFAULT 'webhook',
    payload jsonb,
    occurred_at timestamptz NOT NULL DEFAULT now(),
    created_at timestamptz NOT NULL DEFAULT now(),
    created_by uuid,
    CONSTRAINT messaging_tier_events_pkey PRIMARY KEY (id),
    CONSTRAINT messaging_tier_events_tenant_id_fkey FOREIGN KEY (tenant_id)
    REFERENCES tenancy.tenants (id) ON DELETE RESTRICT,
    CONSTRAINT messaging_tier_events_phone_number_id_fkey
    FOREIGN KEY (phone_number_id)
    REFERENCES waba.phone_numbers (id) ON DELETE CASCADE,
    CONSTRAINT messaging_tier_events_new_tier_check CHECK (
        new_tier IN (
            'tier_250', 'tier_1k', 'tier_10k', 'tier_100k', 'tier_unlimited'
        )
    ),
    CONSTRAINT messaging_tier_events_old_tier_check CHECK (
        old_tier IN (
            'tier_250', 'tier_1k', 'tier_10k', 'tier_100k', 'tier_unlimited'
        )
    ),
    CONSTRAINT messaging_tier_events_event_source_check CHECK (
        event_source IN ('webhook', 'manual', 'simulated')
    )
);

CREATE INDEX messaging_tier_events_number_occurred_at_idx
ON quality.messaging_tier_events (phone_number_id, occurred_at DESC);
CREATE INDEX messaging_tier_events_tenant_id_idx
ON quality.messaging_tier_events (tenant_id);

------------------------------------------------------------------------------
-- guardian_incidents: an open incident when Guardian throttles a number
-- (spec §4.6). throttle_action records what the gateway was told to enforce;
-- status tracks the incident lifecycle.
------------------------------------------------------------------------------
CREATE TABLE quality.guardian_incidents (
    id uuid NOT NULL DEFAULT gen_random_uuid(),
    tenant_id uuid NOT NULL,
    phone_number_id uuid NOT NULL,
    incident_type varchar(30) NOT NULL,
    severity varchar(10) NOT NULL DEFAULT 'warning',
    status varchar(20) NOT NULL DEFAULT 'open',
    throttle_action varchar(30) NOT NULL DEFAULT 'none',
    trigger_rating varchar(10),
    details jsonb,
    opened_at timestamptz NOT NULL DEFAULT now(),
    resolved_at timestamptz,
    created_at timestamptz NOT NULL DEFAULT now(),
    updated_at timestamptz NOT NULL DEFAULT now(),
    created_by uuid,
    updated_by uuid,
    version integer NOT NULL DEFAULT 1,
    CONSTRAINT guardian_incidents_pkey PRIMARY KEY (id),
    CONSTRAINT guardian_incidents_tenant_id_fkey FOREIGN KEY (tenant_id)
    REFERENCES tenancy.tenants (id) ON DELETE RESTRICT,
    CONSTRAINT guardian_incidents_phone_number_id_fkey
    FOREIGN KEY (phone_number_id)
    REFERENCES waba.phone_numbers (id) ON DELETE CASCADE,
    CONSTRAINT guardian_incidents_incident_type_check CHECK (
        incident_type IN (
            'quality_yellow', 'quality_red', 'tier_downgrade',
            'block_rate_spike', 'template_paused'
        )
    ),
    CONSTRAINT guardian_incidents_severity_check CHECK (
        severity IN ('info', 'warning', 'critical')
    ),
    CONSTRAINT guardian_incidents_status_check CHECK (
        status IN ('open', 'mitigating', 'resolved')
    ),
    CONSTRAINT guardian_incidents_throttle_action_check CHECK (
        throttle_action IN ('none', 'marketing_50pct', 'marketing_frozen')
    )
);

CREATE INDEX guardian_incidents_number_opened_at_idx
ON quality.guardian_incidents (phone_number_id, opened_at DESC);
-- Guardian's hot path: the still-active incidents for a tenant.
CREATE INDEX guardian_incidents_tenant_open_idx
ON quality.guardian_incidents (tenant_id)
WHERE status <> 'resolved';

------------------------------------------------------------------------------
-- health_snapshots: weekly per-number health report (spec §4.6). Append-only;
-- one row per (number, period_start).
------------------------------------------------------------------------------
CREATE TABLE quality.health_snapshots (
    id uuid NOT NULL DEFAULT gen_random_uuid(),
    tenant_id uuid NOT NULL,
    phone_number_id uuid NOT NULL,
    period_start date NOT NULL,
    period_end date NOT NULL,
    delivery_rate numeric(5, 2),
    read_rate numeric(5, 2),
    block_proxy_rate numeric(5, 2),
    quality_rating varchar(10),
    messaging_tier varchar(20),
    tier_headroom bigint,
    messages_sent bigint NOT NULL DEFAULT 0,
    messages_delivered bigint NOT NULL DEFAULT 0,
    messages_read bigint NOT NULL DEFAULT 0,
    messages_failed bigint NOT NULL DEFAULT 0,
    metrics jsonb,
    created_at timestamptz NOT NULL DEFAULT now(),
    created_by uuid,
    CONSTRAINT health_snapshots_pkey PRIMARY KEY (id),
    CONSTRAINT health_snapshots_tenant_id_fkey FOREIGN KEY (tenant_id)
    REFERENCES tenancy.tenants (id) ON DELETE RESTRICT,
    CONSTRAINT health_snapshots_phone_number_id_fkey
    FOREIGN KEY (phone_number_id)
    REFERENCES waba.phone_numbers (id) ON DELETE CASCADE,
    CONSTRAINT health_snapshots_number_period_start_key
    UNIQUE (phone_number_id, period_start)
);

CREATE INDEX health_snapshots_tenant_id_idx
ON quality.health_snapshots (tenant_id);

------------------------------------------------------------------------------
-- Row-Level Security (strict pattern)
------------------------------------------------------------------------------
ALTER TABLE quality.number_quality_events ENABLE ROW LEVEL SECURITY;
ALTER TABLE quality.number_quality_events FORCE ROW LEVEL SECURITY;

CREATE POLICY tenant_isolation ON quality.number_quality_events
USING (tenant_id = app.current_tenant_id() OR app.is_platform_admin())
WITH CHECK (tenant_id = app.current_tenant_id() OR app.is_platform_admin());

ALTER TABLE quality.messaging_tier_events ENABLE ROW LEVEL SECURITY;
ALTER TABLE quality.messaging_tier_events FORCE ROW LEVEL SECURITY;

CREATE POLICY tenant_isolation ON quality.messaging_tier_events
USING (tenant_id = app.current_tenant_id() OR app.is_platform_admin())
WITH CHECK (tenant_id = app.current_tenant_id() OR app.is_platform_admin());

ALTER TABLE quality.guardian_incidents ENABLE ROW LEVEL SECURITY;
ALTER TABLE quality.guardian_incidents FORCE ROW LEVEL SECURITY;

CREATE POLICY tenant_isolation ON quality.guardian_incidents
USING (tenant_id = app.current_tenant_id() OR app.is_platform_admin())
WITH CHECK (tenant_id = app.current_tenant_id() OR app.is_platform_admin());

ALTER TABLE quality.health_snapshots ENABLE ROW LEVEL SECURITY;
ALTER TABLE quality.health_snapshots FORCE ROW LEVEL SECURITY;

CREATE POLICY tenant_isolation ON quality.health_snapshots
USING (tenant_id = app.current_tenant_id() OR app.is_platform_admin())
WITH CHECK (tenant_id = app.current_tenant_id() OR app.is_platform_admin());

------------------------------------------------------------------------------
-- Grants
------------------------------------------------------------------------------
GRANT SELECT, INSERT, UPDATE, DELETE ON ALL TABLES IN SCHEMA quality TO app_user;
GRANT SELECT, INSERT, UPDATE, DELETE ON ALL TABLES IN SCHEMA quality
TO platform_admin;

INSERT INTO public.schema_migrations (version)
VALUES ('V011')
ON CONFLICT (version) DO NOTHING;
