-- V007__messaging.sql
-- Schema: messaging — outbound_messages, outbound_outbox, inbound_messages,
-- message_statuses, media_assets, suppression_list (spec §6, §4.2, §4.3;
-- issues #13/#14).
--
-- wamid (Meta's message id, varchar) is the correlation chain across
-- outbound_messages → message_statuses → (Wave 2) billing.message_costs.
-- The message_costs.wamid UNIQUE constraint belongs to the billing schema
-- (Wave 2) — message_statuses only captures the raw pricing/conversation
-- objects; billing is the source of truth for cost (spec §4.3).
--
-- Idempotency ("unique per tenant per 24h", spec §6/§4.2): a partial unique
-- index cannot reference now() (predicates must be immutable), so the 24h
-- window is implemented as
--   partial UNIQUE (tenant_id, idempotency_key) WHERE idempotency_active
-- plus a system.jobs task that clears idempotency_active on rows older than
-- 24h (see db/README.md "Retention / partitioning"). Within the window a
-- duplicate key hits the unique violation and the gateway returns the
-- original result (issue #14).
--
-- template_id / template_version_id FKs are added by V009 (templates schema
-- does not exist yet) — same deferred-FK pattern as V004→V005.
--
-- RLS: all tables tenant-scoped EXCEPT outbound_outbox (the dispatcher is a
-- platform-level worker draining all tenants' queues in one scan — same
-- documented rationale as kernel.outbox_events). Webhook-driven writers
-- (status reconciler, inbound persister) DO run under RLS: every bus event
-- carries tenant_id and the consumer sets the GUC per event.

CREATE SCHEMA messaging;

GRANT USAGE ON SCHEMA messaging TO app_user;
GRANT USAGE ON SCHEMA messaging TO platform_admin;

------------------------------------------------------------------------------
-- outbound_messages: one row per accepted POST /v1/messages request.
------------------------------------------------------------------------------
CREATE TABLE messaging.outbound_messages (
    id uuid NOT NULL DEFAULT gen_random_uuid(),
    tenant_id uuid NOT NULL,
    phone_number_id uuid NOT NULL,
    to_wa_id varchar(20) NOT NULL,
    message_type varchar(30) NOT NULL,
    template_id uuid,
    template_version_id uuid,
    payload jsonb NOT NULL,
    idempotency_key varchar(100) NOT NULL,
    idempotency_active boolean NOT NULL DEFAULT TRUE,
    status varchar(20) NOT NULL DEFAULT 'accepted',
    wamid varchar(128),
    billable_estimate boolean,
    error_code varchar(20),
    error_message text,
    accepted_at timestamptz NOT NULL DEFAULT now(),
    dispatched_at timestamptz,
    created_at timestamptz NOT NULL DEFAULT now(),
    updated_at timestamptz NOT NULL DEFAULT now(),
    created_by uuid,
    updated_by uuid,
    version integer NOT NULL DEFAULT 1,
    CONSTRAINT outbound_messages_pkey PRIMARY KEY (id),
    CONSTRAINT outbound_messages_tenant_id_fkey FOREIGN KEY (tenant_id)
    REFERENCES tenancy.tenants (id) ON DELETE RESTRICT,
    CONSTRAINT outbound_messages_phone_number_id_fkey FOREIGN KEY (phone_number_id)
    REFERENCES waba.phone_numbers (id) ON DELETE RESTRICT,
    CONSTRAINT outbound_messages_message_type_check CHECK (
        message_type IN (
            'text', 'template', 'media',
            'interactive_buttons', 'interactive_list', 'interactive_cta_url',
            'interactive_flow', 'location', 'contacts', 'reaction',
            'order_details'
        )
    ),
    CONSTRAINT outbound_messages_status_check CHECK (
        status IN (
            'accepted', 'dispatched', 'sent', 'delivered', 'read',
            'failed', 'rejected'
        )
    )
);

-- 24h idempotency window (see file header for the mechanism).
CREATE UNIQUE INDEX outbound_messages_tenant_id_idempotency_key_key
ON messaging.outbound_messages (tenant_id, idempotency_key)
WHERE idempotency_active;

-- wamid is assigned only after Graph accepts the send.
CREATE UNIQUE INDEX outbound_messages_wamid_key
ON messaging.outbound_messages (wamid)
WHERE wamid IS NOT NULL;

CREATE INDEX outbound_messages_tenant_id_accepted_at_idx
ON messaging.outbound_messages (tenant_id, accepted_at DESC);
CREATE INDEX outbound_messages_phone_number_id_idx
ON messaging.outbound_messages (phone_number_id);
CREATE INDEX outbound_messages_template_id_idx
ON messaging.outbound_messages (template_id);
CREATE INDEX outbound_messages_template_version_id_idx
ON messaging.outbound_messages (template_version_id);

COMMENT ON COLUMN messaging.outbound_messages.status IS
'Rollup lifecycle: accepted -> dispatched (Graph accepted, wamid set) ->
sent -> delivered -> read, or failed/rejected. Per-event detail lives in
message_statuses; rejected = pre-dispatch policy rejection (WINDOW_CLOSED,
suppression hit, tier exhausted).';

------------------------------------------------------------------------------
-- outbound_outbox: transactional dispatch queue (spec §4.2 outbox pattern).
-- Written in the same transaction as outbound_messages; drained by the
-- gateway dispatcher; NOT RLS-scoped (platform worker, no tenant context).
------------------------------------------------------------------------------
CREATE TABLE messaging.outbound_outbox (
    id uuid NOT NULL DEFAULT gen_random_uuid(),
    tenant_id uuid NOT NULL,
    outbound_message_id uuid NOT NULL,
    phone_number_id uuid NOT NULL,
    status varchar(20) NOT NULL DEFAULT 'pending',
    attempts smallint NOT NULL DEFAULT 0,
    max_attempts smallint NOT NULL DEFAULT 5,
    next_attempt_at timestamptz NOT NULL DEFAULT now(),
    locked_by varchar(100),
    locked_at timestamptz,
    last_error_code varchar(20),
    last_error text,
    created_at timestamptz NOT NULL DEFAULT now(),
    updated_at timestamptz NOT NULL DEFAULT now(),
    created_by uuid,
    updated_by uuid,
    CONSTRAINT outbound_outbox_pkey PRIMARY KEY (id),
    CONSTRAINT outbound_outbox_tenant_id_fkey FOREIGN KEY (tenant_id)
    REFERENCES tenancy.tenants (id) ON DELETE RESTRICT,
    CONSTRAINT outbound_outbox_outbound_message_id_fkey
    FOREIGN KEY (outbound_message_id)
    REFERENCES messaging.outbound_messages (id) ON DELETE CASCADE,
    CONSTRAINT outbound_outbox_outbound_message_id_key
    UNIQUE (outbound_message_id),
    CONSTRAINT outbound_outbox_phone_number_id_fkey FOREIGN KEY (phone_number_id)
    REFERENCES waba.phone_numbers (id) ON DELETE RESTRICT,
    CONSTRAINT outbound_outbox_status_check CHECK (
        status IN ('pending', 'dispatching', 'dispatched', 'failed', 'dead')
    )
);

-- Dispatcher scan: due pending work, oldest first. phone_number_id feeds the
-- per-number token bucket (spec §4.2).
CREATE INDEX outbound_outbox_due_idx
ON messaging.outbound_outbox (next_attempt_at)
WHERE status IN ('pending', 'dispatching');
CREATE INDEX outbound_outbox_phone_number_id_idx
ON messaging.outbound_outbox (phone_number_id);
CREATE INDEX outbound_outbox_tenant_id_idx
ON messaging.outbound_outbox (tenant_id);

COMMENT ON TABLE messaging.outbound_outbox IS
'Graph-API dispatch queue (distinct from kernel.outbox_events, the domain
event outbox). status: pending -> dispatching (leased via locked_by/locked_at)
-> dispatched | failed (retryable, next_attempt_at backoff) | dead (permanent
error 131026/131047/131049 or max_attempts exhausted). Deliberately NOT
RLS-scoped: the dispatcher drains all tenants without a tenant context.';

------------------------------------------------------------------------------
-- inbound_messages: normalized inbound messages (append-only; the referral
-- object drives CTWA 72h windows in the sessions schema).
------------------------------------------------------------------------------
CREATE TABLE messaging.inbound_messages (
    id uuid NOT NULL DEFAULT gen_random_uuid(),
    tenant_id uuid NOT NULL,
    phone_number_id uuid NOT NULL,
    wamid varchar(128) NOT NULL,
    from_wa_id varchar(20) NOT NULL,
    message_type varchar(30) NOT NULL,
    payload jsonb NOT NULL,
    context jsonb,
    referral jsonb,
    received_at timestamptz NOT NULL,
    created_at timestamptz NOT NULL DEFAULT now(),
    created_by uuid,
    CONSTRAINT inbound_messages_pkey PRIMARY KEY (id),
    CONSTRAINT inbound_messages_tenant_id_fkey FOREIGN KEY (tenant_id)
    REFERENCES tenancy.tenants (id) ON DELETE RESTRICT,
    CONSTRAINT inbound_messages_phone_number_id_fkey FOREIGN KEY (phone_number_id)
    REFERENCES waba.phone_numbers (id) ON DELETE RESTRICT,
    CONSTRAINT inbound_messages_wamid_key UNIQUE (wamid)
);

-- Session Window Manager lookup: latest inbound per (number, consumer).
CREATE INDEX inbound_messages_window_lookup_idx
ON messaging.inbound_messages (phone_number_id, from_wa_id, received_at DESC);
CREATE INDEX inbound_messages_tenant_id_received_at_idx
ON messaging.inbound_messages (tenant_id, received_at DESC);

------------------------------------------------------------------------------
-- message_statuses: per-event delivery statuses from webhooks (append-only).
-- pricing/conversation are raw captures; billing.message_costs (Wave 2) is
-- the billing source of truth keyed by wamid.
------------------------------------------------------------------------------
CREATE TABLE messaging.message_statuses (
    id uuid NOT NULL DEFAULT gen_random_uuid(),
    tenant_id uuid NOT NULL,
    outbound_message_id uuid,
    wamid varchar(128) NOT NULL,
    status varchar(20) NOT NULL,
    recipient_wa_id varchar(20),
    error_code varchar(20),
    error_detail jsonb,
    conversation jsonb,
    pricing jsonb,
    occurred_at timestamptz NOT NULL,
    created_at timestamptz NOT NULL DEFAULT now(),
    created_by uuid,
    CONSTRAINT message_statuses_pkey PRIMARY KEY (id),
    CONSTRAINT message_statuses_tenant_id_fkey FOREIGN KEY (tenant_id)
    REFERENCES tenancy.tenants (id) ON DELETE RESTRICT,
    CONSTRAINT message_statuses_outbound_message_id_fkey
    FOREIGN KEY (outbound_message_id)
    REFERENCES messaging.outbound_messages (id) ON DELETE CASCADE,
    CONSTRAINT message_statuses_status_check CHECK (
        status IN ('sent', 'delivered', 'read', 'failed')
    )
);

CREATE INDEX message_statuses_wamid_idx ON messaging.message_statuses (wamid);
CREATE INDEX message_statuses_outbound_message_id_occurred_at_idx
ON messaging.message_statuses (outbound_message_id, occurred_at DESC);
CREATE INDEX message_statuses_tenant_id_idx
ON messaging.message_statuses (tenant_id);

------------------------------------------------------------------------------
-- media_assets: Meta media handles bridged to stored bytes
-- (kernel.file_attachments). Meta media URLs/ids expire — meta_media_id is
-- indexed but NOT unique (ids can be re-issued after expiry).
------------------------------------------------------------------------------
CREATE TABLE messaging.media_assets (
    id uuid NOT NULL DEFAULT gen_random_uuid(),
    tenant_id uuid NOT NULL,
    direction varchar(10) NOT NULL,
    meta_media_id varchar(64),
    file_attachment_id uuid,
    file_name varchar(500),
    mime_type varchar(100),
    bytes bigint,
    sha256 character(64),
    caption text,
    status varchar(20) NOT NULL DEFAULT 'pending',
    expires_at timestamptz,
    created_at timestamptz NOT NULL DEFAULT now(),
    updated_at timestamptz NOT NULL DEFAULT now(),
    created_by uuid,
    updated_by uuid,
    version integer NOT NULL DEFAULT 1,
    CONSTRAINT media_assets_pkey PRIMARY KEY (id),
    CONSTRAINT media_assets_tenant_id_fkey FOREIGN KEY (tenant_id)
    REFERENCES tenancy.tenants (id) ON DELETE RESTRICT,
    CONSTRAINT media_assets_file_attachment_id_fkey
    FOREIGN KEY (file_attachment_id)
    REFERENCES kernel.file_attachments (id) ON DELETE SET NULL,
    CONSTRAINT media_assets_direction_check CHECK (
        direction IN ('inbound', 'outbound')
    ),
    CONSTRAINT media_assets_status_check CHECK (
        status IN ('pending', 'downloaded', 'uploaded', 'failed', 'expired')
    )
);

CREATE INDEX media_assets_tenant_id_idx ON messaging.media_assets (tenant_id);
CREATE INDEX media_assets_meta_media_id_idx
ON messaging.media_assets (meta_media_id);
CREATE INDEX media_assets_file_attachment_id_idx
ON messaging.media_assets (file_attachment_id);

------------------------------------------------------------------------------
-- suppression_list: per-tenant do-not-send registry, checked by the gateway
-- pre-dispatch. expires_at NULL = permanent suppression.
------------------------------------------------------------------------------
CREATE TABLE messaging.suppression_list (
    id uuid NOT NULL DEFAULT gen_random_uuid(),
    tenant_id uuid NOT NULL,
    wa_id varchar(20) NOT NULL,
    reason varchar(30) NOT NULL,
    source varchar(50),
    notes text,
    expires_at timestamptz,
    created_at timestamptz NOT NULL DEFAULT now(),
    updated_at timestamptz NOT NULL DEFAULT now(),
    created_by uuid,
    updated_by uuid,
    CONSTRAINT suppression_list_pkey PRIMARY KEY (id),
    CONSTRAINT suppression_list_tenant_id_fkey FOREIGN KEY (tenant_id)
    REFERENCES tenancy.tenants (id) ON DELETE CASCADE,
    CONSTRAINT suppression_list_tenant_id_wa_id_key UNIQUE (tenant_id, wa_id),
    CONSTRAINT suppression_list_reason_check CHECK (
        reason IN (
            'opt_out', 'stop_keyword', 'hard_error', 'complaint',
            'compliance', 'manual'
        )
    )
);

------------------------------------------------------------------------------
-- Row-Level Security (strict pattern; outbound_outbox deliberately excluded —
-- see its table comment).
------------------------------------------------------------------------------
ALTER TABLE messaging.outbound_messages ENABLE ROW LEVEL SECURITY;
ALTER TABLE messaging.outbound_messages FORCE ROW LEVEL SECURITY;

CREATE POLICY tenant_isolation ON messaging.outbound_messages
USING (tenant_id = app.current_tenant_id() OR app.is_platform_admin())
WITH CHECK (tenant_id = app.current_tenant_id() OR app.is_platform_admin());

ALTER TABLE messaging.inbound_messages ENABLE ROW LEVEL SECURITY;
ALTER TABLE messaging.inbound_messages FORCE ROW LEVEL SECURITY;

CREATE POLICY tenant_isolation ON messaging.inbound_messages
USING (tenant_id = app.current_tenant_id() OR app.is_platform_admin())
WITH CHECK (tenant_id = app.current_tenant_id() OR app.is_platform_admin());

ALTER TABLE messaging.message_statuses ENABLE ROW LEVEL SECURITY;
ALTER TABLE messaging.message_statuses FORCE ROW LEVEL SECURITY;

CREATE POLICY tenant_isolation ON messaging.message_statuses
USING (tenant_id = app.current_tenant_id() OR app.is_platform_admin())
WITH CHECK (tenant_id = app.current_tenant_id() OR app.is_platform_admin());

ALTER TABLE messaging.media_assets ENABLE ROW LEVEL SECURITY;
ALTER TABLE messaging.media_assets FORCE ROW LEVEL SECURITY;

CREATE POLICY tenant_isolation ON messaging.media_assets
USING (tenant_id = app.current_tenant_id() OR app.is_platform_admin())
WITH CHECK (tenant_id = app.current_tenant_id() OR app.is_platform_admin());

ALTER TABLE messaging.suppression_list ENABLE ROW LEVEL SECURITY;
ALTER TABLE messaging.suppression_list FORCE ROW LEVEL SECURITY;

CREATE POLICY tenant_isolation ON messaging.suppression_list
USING (tenant_id = app.current_tenant_id() OR app.is_platform_admin())
WITH CHECK (tenant_id = app.current_tenant_id() OR app.is_platform_admin());

------------------------------------------------------------------------------
-- Grants
------------------------------------------------------------------------------
GRANT SELECT, INSERT, UPDATE, DELETE ON ALL TABLES IN SCHEMA messaging TO app_user;
GRANT SELECT, INSERT, UPDATE, DELETE ON ALL TABLES IN SCHEMA messaging
TO platform_admin;

INSERT INTO public.schema_migrations (version)
VALUES ('V007')
ON CONFLICT (version) DO NOTHING;
