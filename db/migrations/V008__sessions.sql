-- V008__sessions.sql
-- Schema: sessions — conversation_windows, window_events (spec §6, §4.5;
-- issue #15: Session Window Manager).
--
-- One row per (tenant_id, phone_number_id, user_wa_id) pair, maintained by
-- UPSERT onto the unique key below (spec §6: "single active row per pair,
-- upsert" — no EXCLUDE constraint needed). The row tracks BOTH window kinds:
--   cs_expires_at   — customer-service window: last inbound + 24h, reset on
--                     every consumer message
--   ctwa_expires_at — click-to-WhatsApp window: referral entry + 72h
-- A window is open iff the respective expiry is in the future.
--
-- RLS: both tables are tenant-scoped (strict pattern). NOTE for the service
-- (recorded in handoff): the wa.window.closing emitter scans ALL tenants for
-- windows expiring within 2h — a background job with no tenant context. It
-- must run on a DB role granted platform_admin (audited cross-tenant read),
-- or iterate tenants setting the GUC per tenant. app_user alone will see
-- nothing, by design.

CREATE SCHEMA sessions;

GRANT USAGE ON SCHEMA sessions TO app_user;
GRANT USAGE ON SCHEMA sessions TO platform_admin;

------------------------------------------------------------------------------
-- conversation_windows
------------------------------------------------------------------------------
CREATE TABLE sessions.conversation_windows (
    id uuid NOT NULL DEFAULT gen_random_uuid(),
    tenant_id uuid NOT NULL,
    phone_number_id uuid NOT NULL,
    user_wa_id varchar(20) NOT NULL,
    origin varchar(10) NOT NULL DEFAULT 'organic',
    cs_expires_at timestamptz,
    cs_last_inbound_at timestamptz,
    ctwa_expires_at timestamptz,
    ctwa_entered_at timestamptz,
    ctwa_referral jsonb,
    closing_notified_at timestamptz,
    is_simulated boolean NOT NULL DEFAULT FALSE,
    created_at timestamptz NOT NULL DEFAULT now(),
    updated_at timestamptz NOT NULL DEFAULT now(),
    created_by uuid,
    updated_by uuid,
    version integer NOT NULL DEFAULT 1,
    CONSTRAINT conversation_windows_pkey PRIMARY KEY (id),
    CONSTRAINT conversation_windows_tenant_id_fkey FOREIGN KEY (tenant_id)
    REFERENCES tenancy.tenants (id) ON DELETE RESTRICT,
    CONSTRAINT conversation_windows_phone_number_id_fkey
    FOREIGN KEY (phone_number_id)
    REFERENCES waba.phone_numbers (id) ON DELETE CASCADE,
    -- The upsert target: single active row per pair.
    CONSTRAINT conversation_windows_pair_key
    UNIQUE (tenant_id, phone_number_id, user_wa_id),
    CONSTRAINT conversation_windows_origin_check CHECK (
        origin IN ('organic', 'ctwa', 'fb_cta')
    )
);

-- wa.window.closing scan (2h before expiry, spec §4.5): open windows by
-- soonest expiry. Partial: expired-forever rows (both NULL) are skipped.
CREATE INDEX conversation_windows_cs_expiry_idx
ON sessions.conversation_windows (cs_expires_at)
WHERE cs_expires_at IS NOT NULL;
CREATE INDEX conversation_windows_ctwa_expiry_idx
ON sessions.conversation_windows (ctwa_expires_at)
WHERE ctwa_expires_at IS NOT NULL;

COMMENT ON COLUMN sessions.conversation_windows.is_simulated IS
'true only for rows fabricated by the non-prod simulation endpoint
(issue #15 QA); must never be true in production.';

COMMENT ON COLUMN sessions.conversation_windows.closing_notified_at IS
'When wa.window.closing was emitted for the CURRENT window; reset to NULL
whenever cs_expires_at/ctwa_expires_at is extended, so re-opened windows get
a fresh closing notification.';

------------------------------------------------------------------------------
-- window_events: append-only history of window transitions (debug/audit +
-- QA verification of CS/CTWA scenarios).
------------------------------------------------------------------------------
CREATE TABLE sessions.window_events (
    id uuid NOT NULL DEFAULT gen_random_uuid(),
    tenant_id uuid NOT NULL,
    conversation_window_id uuid NOT NULL,
    event_type varchar(30) NOT NULL,
    old_expires_at timestamptz,
    new_expires_at timestamptz,
    payload jsonb,
    occurred_at timestamptz NOT NULL DEFAULT now(),
    created_at timestamptz NOT NULL DEFAULT now(),
    created_by uuid,
    CONSTRAINT window_events_pkey PRIMARY KEY (id),
    CONSTRAINT window_events_tenant_id_fkey FOREIGN KEY (tenant_id)
    REFERENCES tenancy.tenants (id) ON DELETE RESTRICT,
    CONSTRAINT window_events_conversation_window_id_fkey
    FOREIGN KEY (conversation_window_id)
    REFERENCES sessions.conversation_windows (id) ON DELETE CASCADE,
    CONSTRAINT window_events_event_type_check CHECK (
        event_type IN (
            'cs_opened', 'cs_extended', 'cs_expired',
            'ctwa_opened', 'ctwa_extended', 'ctwa_expired',
            'closing_notified', 'simulated'
        )
    )
);

CREATE INDEX window_events_conversation_window_id_occurred_at_idx
ON sessions.window_events (conversation_window_id, occurred_at DESC);
CREATE INDEX window_events_tenant_id_idx ON sessions.window_events (tenant_id);

------------------------------------------------------------------------------
-- Row-Level Security (strict pattern)
------------------------------------------------------------------------------
ALTER TABLE sessions.conversation_windows ENABLE ROW LEVEL SECURITY;
ALTER TABLE sessions.conversation_windows FORCE ROW LEVEL SECURITY;

CREATE POLICY tenant_isolation ON sessions.conversation_windows
USING (tenant_id = app.current_tenant_id() OR app.is_platform_admin())
WITH CHECK (tenant_id = app.current_tenant_id() OR app.is_platform_admin());

ALTER TABLE sessions.window_events ENABLE ROW LEVEL SECURITY;
ALTER TABLE sessions.window_events FORCE ROW LEVEL SECURITY;

CREATE POLICY tenant_isolation ON sessions.window_events
USING (tenant_id = app.current_tenant_id() OR app.is_platform_admin())
WITH CHECK (tenant_id = app.current_tenant_id() OR app.is_platform_admin());

------------------------------------------------------------------------------
-- Grants
------------------------------------------------------------------------------
GRANT SELECT, INSERT, UPDATE, DELETE ON ALL TABLES IN SCHEMA sessions TO app_user;
GRANT SELECT, INSERT, UPDATE, DELETE ON ALL TABLES IN SCHEMA sessions
TO platform_admin;

INSERT INTO public.schema_migrations (version)
VALUES ('V008')
ON CONFLICT (version) DO NOTHING;
