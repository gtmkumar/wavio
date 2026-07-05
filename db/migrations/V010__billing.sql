-- V010__billing.sql
-- Schema: billing — rate_cards, rate_card_entries, message_costs,
-- tenant_quotas, usage_counters, payment_transactions, invoices_feed,
-- max_price_configs (spec §6, §4.7, §2.1; issue #19, #23).
--
-- PMP (per-message pricing) billing, day one — ADR-002. The webhook `pricing`
-- object is the billing source of truth (spec §4.7): it is written verbatim
-- into message_costs; our rate cards only drive the pre-send ESTIMATOR, never
-- the ledger. message_costs.wamid is UNIQUE (spec §6 key constraint).
--
-- RLS: message_costs, tenant_quotas, usage_counters, payment_transactions,
-- invoices_feed, max_price_configs are tenant-scoped (strict pattern).
-- rate_cards / rate_card_entries are PLATFORM-GLOBAL reference data — Meta's
-- rate card is identical for every tenant, so they carry no tenant_id and no
-- RLS (same posture as ingest.webhook_dedupe), only grants.

CREATE SCHEMA billing;

GRANT USAGE ON SCHEMA billing TO app_user;
GRANT USAGE ON SCHEMA billing TO platform_admin;

------------------------------------------------------------------------------
-- rate_cards: a versioned, effective-dated snapshot of Meta's rate card
-- (spec §4.7). Future-dated cards load in advance (Meta gives >=1 quarter
-- notice); the quarterly refresh job (issue #19) inserts the next card.
-- GLOBAL: no tenant_id.
------------------------------------------------------------------------------
CREATE TABLE billing.rate_cards (
    id uuid NOT NULL DEFAULT gen_random_uuid(),
    name varchar(120) NOT NULL,
    currency varchar(3) NOT NULL DEFAULT 'INR',
    source varchar(20) NOT NULL DEFAULT 'meta',
    effective_from date NOT NULL,
    effective_to date,
    status varchar(20) NOT NULL DEFAULT 'active',
    notes text,
    created_at timestamptz NOT NULL DEFAULT now(),
    updated_at timestamptz NOT NULL DEFAULT now(),
    created_by uuid,
    updated_by uuid,
    version integer NOT NULL DEFAULT 1,
    CONSTRAINT rate_cards_pkey PRIMARY KEY (id),
    CONSTRAINT rate_cards_currency_effective_from_key
    UNIQUE (currency, effective_from),
    CONSTRAINT rate_cards_source_check CHECK (
        source IN ('meta', 'manual')
    ),
    CONSTRAINT rate_cards_status_check CHECK (
        status IN ('draft', 'active', 'superseded')
    )
);

CREATE INDEX rate_cards_effective_from_idx
ON billing.rate_cards (effective_from DESC);

------------------------------------------------------------------------------
-- rate_card_entries: one price per category x market x volume tier within a
-- card (spec §4.7). volume_tier NULL = tier-agnostic (marketing assumes no
-- discounts; utility/auth track tiered discounts). GLOBAL: no tenant_id.
------------------------------------------------------------------------------
CREATE TABLE billing.rate_card_entries (
    id uuid NOT NULL DEFAULT gen_random_uuid(),
    rate_card_id uuid NOT NULL,
    category varchar(30) NOT NULL,
    market varchar(60) NOT NULL,
    volume_tier varchar(20),
    price_per_message numeric(12, 6) NOT NULL,
    currency varchar(3) NOT NULL DEFAULT 'INR',
    created_at timestamptz NOT NULL DEFAULT now(),
    updated_at timestamptz NOT NULL DEFAULT now(),
    created_by uuid,
    updated_by uuid,
    version integer NOT NULL DEFAULT 1,
    CONSTRAINT rate_card_entries_pkey PRIMARY KEY (id),
    CONSTRAINT rate_card_entries_rate_card_id_fkey FOREIGN KEY (rate_card_id)
    REFERENCES billing.rate_cards (id) ON DELETE CASCADE,
    CONSTRAINT rate_card_entries_category_check CHECK (
        category IN (
            'marketing', 'utility', 'authentication',
            'authentication_international', 'service'
        )
    )
);

-- volume_tier NULL must not collide with itself: one entry per
-- (card, category, market, tier), NULL tier being a distinct slot.
CREATE UNIQUE INDEX rate_card_entries_card_category_market_tier_key
ON billing.rate_card_entries (rate_card_id, category, market, volume_tier)
NULLS NOT DISTINCT;

------------------------------------------------------------------------------
-- message_costs: the PMP cost ledger — one row per billed delivery, straight
-- from the status webhook `pricing` object (spec §4.7, §6). Append-only.
-- wamid is the Meta message id and is globally UNIQUE (spec §6). rate_card_id
-- records which card the advisory estimate used, if any (SET NULL on cleanup).
------------------------------------------------------------------------------
CREATE TABLE billing.message_costs (
    id uuid NOT NULL DEFAULT gen_random_uuid(),
    tenant_id uuid NOT NULL,
    phone_number_id uuid NOT NULL,
    rate_card_id uuid,
    wamid varchar(128) NOT NULL,
    category varchar(30) NOT NULL,
    pricing_model varchar(20),
    pricing_category varchar(40),
    billable boolean NOT NULL DEFAULT TRUE,
    amount numeric(12, 6) NOT NULL DEFAULT 0,
    currency varchar(3) NOT NULL DEFAULT 'INR',
    destination_market varchar(60),
    webhook_pricing jsonb,
    billed_at timestamptz NOT NULL DEFAULT now(),
    created_at timestamptz NOT NULL DEFAULT now(),
    created_by uuid,
    CONSTRAINT message_costs_pkey PRIMARY KEY (id),
    CONSTRAINT message_costs_tenant_id_fkey FOREIGN KEY (tenant_id)
    REFERENCES tenancy.tenants (id) ON DELETE RESTRICT,
    CONSTRAINT message_costs_phone_number_id_fkey FOREIGN KEY (phone_number_id)
    REFERENCES waba.phone_numbers (id) ON DELETE CASCADE,
    CONSTRAINT message_costs_rate_card_id_fkey FOREIGN KEY (rate_card_id)
    REFERENCES billing.rate_cards (id) ON DELETE SET NULL,
    CONSTRAINT message_costs_wamid_key UNIQUE (wamid),
    CONSTRAINT message_costs_category_check CHECK (
        category IN (
            'marketing', 'utility', 'authentication',
            'authentication_international', 'service'
        )
    )
);

CREATE INDEX message_costs_tenant_id_billed_at_idx
ON billing.message_costs (tenant_id, billed_at DESC);
CREATE INDEX message_costs_phone_number_id_idx
ON billing.message_costs (phone_number_id);

COMMENT ON COLUMN billing.message_costs.webhook_pricing IS
'Raw Meta status-webhook `pricing` object — the billing source of truth
(spec §4.7). Our rate-card estimate is advisory only and never overrides it.';

------------------------------------------------------------------------------
-- tenant_quotas: per-tenant metering limits by category (spec §4.7). Soft
-- limit -> alert; hard limit -> marketing block. Utility/service are never
-- blocked, enforced in the gateway; this table just holds the thresholds.
------------------------------------------------------------------------------
CREATE TABLE billing.tenant_quotas (
    id uuid NOT NULL DEFAULT gen_random_uuid(),
    tenant_id uuid NOT NULL,
    category varchar(30) NOT NULL,
    period varchar(10) NOT NULL DEFAULT 'monthly',
    limit_unit varchar(10) NOT NULL DEFAULT 'messages',
    soft_limit bigint,
    hard_limit bigint,
    currency varchar(3),
    enabled boolean NOT NULL DEFAULT TRUE,
    created_at timestamptz NOT NULL DEFAULT now(),
    updated_at timestamptz NOT NULL DEFAULT now(),
    created_by uuid,
    updated_by uuid,
    version integer NOT NULL DEFAULT 1,
    CONSTRAINT tenant_quotas_pkey PRIMARY KEY (id),
    CONSTRAINT tenant_quotas_tenant_id_fkey FOREIGN KEY (tenant_id)
    REFERENCES tenancy.tenants (id) ON DELETE RESTRICT,
    CONSTRAINT tenant_quotas_tenant_id_category_period_key
    UNIQUE (tenant_id, category, period),
    CONSTRAINT tenant_quotas_category_check CHECK (
        category IN ('marketing', 'utility', 'authentication', 'service', 'all')
    ),
    CONSTRAINT tenant_quotas_period_check CHECK (
        period IN ('daily', 'monthly')
    ),
    CONSTRAINT tenant_quotas_limit_unit_check CHECK (
        limit_unit IN ('messages', 'amount')
    )
);

CREATE INDEX tenant_quotas_tenant_id_idx ON billing.tenant_quotas (tenant_id);

------------------------------------------------------------------------------
-- usage_counters: running metered usage per (tenant, category, period). The
-- estimator/quota engine upserts onto the unique key and stamps the *_at
-- columns when soft/hard limits are crossed (spec §4.7).
------------------------------------------------------------------------------
CREATE TABLE billing.usage_counters (
    id uuid NOT NULL DEFAULT gen_random_uuid(),
    tenant_id uuid NOT NULL,
    category varchar(30) NOT NULL,
    period varchar(10) NOT NULL DEFAULT 'monthly',
    period_start date NOT NULL,
    message_count bigint NOT NULL DEFAULT 0,
    billable_amount numeric(14, 6) NOT NULL DEFAULT 0,
    currency varchar(3) NOT NULL DEFAULT 'INR',
    soft_limit_alerted_at timestamptz,
    hard_limit_blocked_at timestamptz,
    created_at timestamptz NOT NULL DEFAULT now(),
    updated_at timestamptz NOT NULL DEFAULT now(),
    created_by uuid,
    updated_by uuid,
    version integer NOT NULL DEFAULT 1,
    CONSTRAINT usage_counters_pkey PRIMARY KEY (id),
    CONSTRAINT usage_counters_tenant_id_fkey FOREIGN KEY (tenant_id)
    REFERENCES tenancy.tenants (id) ON DELETE RESTRICT,
    CONSTRAINT usage_counters_tenant_id_category_period_start_key
    UNIQUE (tenant_id, category, period, period_start),
    CONSTRAINT usage_counters_category_check CHECK (
        category IN ('marketing', 'utility', 'authentication', 'service', 'all')
    ),
    CONSTRAINT usage_counters_period_check CHECK (
        period IN ('daily', 'monthly')
    )
);

CREATE INDEX usage_counters_tenant_id_idx ON billing.usage_counters (tenant_id);

------------------------------------------------------------------------------
-- payment_transactions: settlement/reconciliation ledger per tenant (spec
-- §4.7). external_reference is a PSP/Meta-invoice string, not a local row.
------------------------------------------------------------------------------
CREATE TABLE billing.payment_transactions (
    id uuid NOT NULL DEFAULT gen_random_uuid(),
    tenant_id uuid NOT NULL,
    txn_type varchar(20) NOT NULL,
    direction varchar(10) NOT NULL DEFAULT 'debit',
    amount numeric(14, 4) NOT NULL,
    currency varchar(3) NOT NULL DEFAULT 'INR',
    status varchar(20) NOT NULL DEFAULT 'pending',
    external_reference varchar(128),
    period_start date,
    period_end date,
    description text,
    metadata jsonb,
    occurred_at timestamptz NOT NULL DEFAULT now(),
    settled_at timestamptz,
    created_at timestamptz NOT NULL DEFAULT now(),
    updated_at timestamptz NOT NULL DEFAULT now(),
    created_by uuid,
    updated_by uuid,
    version integer NOT NULL DEFAULT 1,
    CONSTRAINT payment_transactions_pkey PRIMARY KEY (id),
    CONSTRAINT payment_transactions_tenant_id_fkey FOREIGN KEY (tenant_id)
    REFERENCES tenancy.tenants (id) ON DELETE RESTRICT,
    CONSTRAINT payment_transactions_txn_type_check CHECK (
        txn_type IN ('charge', 'credit', 'settlement', 'adjustment')
    ),
    CONSTRAINT payment_transactions_direction_check CHECK (
        direction IN ('debit', 'credit')
    ),
    CONSTRAINT payment_transactions_status_check CHECK (
        status IN ('pending', 'settled', 'failed', 'reversed')
    )
);

CREATE INDEX payment_transactions_tenant_id_occurred_at_idx
ON billing.payment_transactions (tenant_id, occurred_at DESC);

------------------------------------------------------------------------------
-- invoices_feed: GST invoice trail per tenant (spec §11 — GSTIN + HSN/SAC).
-- invoice_number is assigned on issue, so its uniqueness is a partial index.
------------------------------------------------------------------------------
CREATE TABLE billing.invoices_feed (
    id uuid NOT NULL DEFAULT gen_random_uuid(),
    tenant_id uuid NOT NULL,
    invoice_number varchar(64),
    period_start date NOT NULL,
    period_end date NOT NULL,
    gstin varchar(15),
    place_of_supply varchar(60),
    hsn_sac_code varchar(10),
    taxable_amount numeric(14, 2) NOT NULL DEFAULT 0,
    cgst_amount numeric(14, 2) NOT NULL DEFAULT 0,
    sgst_amount numeric(14, 2) NOT NULL DEFAULT 0,
    igst_amount numeric(14, 2) NOT NULL DEFAULT 0,
    total_amount numeric(14, 2) NOT NULL DEFAULT 0,
    currency varchar(3) NOT NULL DEFAULT 'INR',
    status varchar(20) NOT NULL DEFAULT 'draft',
    line_items jsonb NOT NULL DEFAULT '[]',
    issued_at timestamptz,
    created_at timestamptz NOT NULL DEFAULT now(),
    updated_at timestamptz NOT NULL DEFAULT now(),
    created_by uuid,
    updated_by uuid,
    version integer NOT NULL DEFAULT 1,
    CONSTRAINT invoices_feed_pkey PRIMARY KEY (id),
    CONSTRAINT invoices_feed_tenant_id_fkey FOREIGN KEY (tenant_id)
    REFERENCES tenancy.tenants (id) ON DELETE RESTRICT,
    CONSTRAINT invoices_feed_status_check CHECK (
        status IN ('draft', 'issued', 'paid', 'cancelled')
    )
);

CREATE UNIQUE INDEX invoices_feed_tenant_id_invoice_number_key
ON billing.invoices_feed (tenant_id, invoice_number)
WHERE invoice_number IS NOT NULL;
CREATE INDEX invoices_feed_tenant_id_idx ON billing.invoices_feed (tenant_id);

------------------------------------------------------------------------------
-- max_price_configs: per-tenant (optionally per-number) max-price bid config
-- (spec §4.7). Feature-flagged OFF (enabled DEFAULT FALSE) until Meta's open
-- beta ~Oct 2026 (issue #28). phone_number_id NULL = tenant-wide default.
------------------------------------------------------------------------------
CREATE TABLE billing.max_price_configs (
    id uuid NOT NULL DEFAULT gen_random_uuid(),
    tenant_id uuid NOT NULL,
    phone_number_id uuid,
    category varchar(30) NOT NULL DEFAULT 'marketing',
    market varchar(60),
    max_price numeric(12, 6) NOT NULL,
    currency varchar(3) NOT NULL DEFAULT 'INR',
    enabled boolean NOT NULL DEFAULT FALSE,
    effective_from timestamptz,
    effective_to timestamptz,
    created_at timestamptz NOT NULL DEFAULT now(),
    updated_at timestamptz NOT NULL DEFAULT now(),
    created_by uuid,
    updated_by uuid,
    version integer NOT NULL DEFAULT 1,
    CONSTRAINT max_price_configs_pkey PRIMARY KEY (id),
    CONSTRAINT max_price_configs_tenant_id_fkey FOREIGN KEY (tenant_id)
    REFERENCES tenancy.tenants (id) ON DELETE RESTRICT,
    CONSTRAINT max_price_configs_phone_number_id_fkey
    FOREIGN KEY (phone_number_id)
    REFERENCES waba.phone_numbers (id) ON DELETE CASCADE,
    CONSTRAINT max_price_configs_category_check CHECK (
        category IN ('marketing', 'utility', 'authentication', 'service')
    )
);

CREATE UNIQUE INDEX max_price_configs_tenant_number_category_market_key
ON billing.max_price_configs (tenant_id, phone_number_id, category, market)
NULLS NOT DISTINCT;
CREATE INDEX max_price_configs_tenant_id_idx
ON billing.max_price_configs (tenant_id);

------------------------------------------------------------------------------
-- Row-Level Security (strict pattern on tenant-scoped tables only;
-- rate_cards / rate_card_entries are global reference data, no RLS).
------------------------------------------------------------------------------
ALTER TABLE billing.message_costs ENABLE ROW LEVEL SECURITY;
ALTER TABLE billing.message_costs FORCE ROW LEVEL SECURITY;

CREATE POLICY tenant_isolation ON billing.message_costs
USING (tenant_id = app.current_tenant_id() OR app.is_platform_admin())
WITH CHECK (tenant_id = app.current_tenant_id() OR app.is_platform_admin());

ALTER TABLE billing.tenant_quotas ENABLE ROW LEVEL SECURITY;
ALTER TABLE billing.tenant_quotas FORCE ROW LEVEL SECURITY;

CREATE POLICY tenant_isolation ON billing.tenant_quotas
USING (tenant_id = app.current_tenant_id() OR app.is_platform_admin())
WITH CHECK (tenant_id = app.current_tenant_id() OR app.is_platform_admin());

ALTER TABLE billing.usage_counters ENABLE ROW LEVEL SECURITY;
ALTER TABLE billing.usage_counters FORCE ROW LEVEL SECURITY;

CREATE POLICY tenant_isolation ON billing.usage_counters
USING (tenant_id = app.current_tenant_id() OR app.is_platform_admin())
WITH CHECK (tenant_id = app.current_tenant_id() OR app.is_platform_admin());

ALTER TABLE billing.payment_transactions ENABLE ROW LEVEL SECURITY;
ALTER TABLE billing.payment_transactions FORCE ROW LEVEL SECURITY;

CREATE POLICY tenant_isolation ON billing.payment_transactions
USING (tenant_id = app.current_tenant_id() OR app.is_platform_admin())
WITH CHECK (tenant_id = app.current_tenant_id() OR app.is_platform_admin());

ALTER TABLE billing.invoices_feed ENABLE ROW LEVEL SECURITY;
ALTER TABLE billing.invoices_feed FORCE ROW LEVEL SECURITY;

CREATE POLICY tenant_isolation ON billing.invoices_feed
USING (tenant_id = app.current_tenant_id() OR app.is_platform_admin())
WITH CHECK (tenant_id = app.current_tenant_id() OR app.is_platform_admin());

ALTER TABLE billing.max_price_configs ENABLE ROW LEVEL SECURITY;
ALTER TABLE billing.max_price_configs FORCE ROW LEVEL SECURITY;

CREATE POLICY tenant_isolation ON billing.max_price_configs
USING (tenant_id = app.current_tenant_id() OR app.is_platform_admin())
WITH CHECK (tenant_id = app.current_tenant_id() OR app.is_platform_admin());

------------------------------------------------------------------------------
-- Grants
------------------------------------------------------------------------------
GRANT SELECT, INSERT, UPDATE, DELETE ON ALL TABLES IN SCHEMA billing TO app_user;
GRANT SELECT, INSERT, UPDATE, DELETE ON ALL TABLES IN SCHEMA billing
TO platform_admin;

INSERT INTO public.schema_migrations (version)
VALUES ('V010')
ON CONFLICT (version) DO NOTHING;
