-- V003__ingest.sql
-- Schema: ingest — raw_webhooks (30-day TTL), webhook_dedupe (spec §6).
--
-- Deliberately NOT tenant-scoped (no RLS): rows are written by the webhook
-- receiver BEFORE tenant resolution (Meta posts to one platform endpoint; the
-- owning tenant is derived later from the payload's WABA/phone ids). tenant_id
-- is backfilled after resolution and is nullable. The wa-webhook service is a
-- platform-level component; RLS here would blind it to unresolved rows.
--
-- 30-day TTL strategy (OSS-only, no pg_cron/pg_partman dependency): native
-- weekly RANGE partitions on received_at + ingest.maintain_raw_webhooks(),
-- which creates upcoming weekly partitions and DROPs partitions entirely
-- older than the retention window. Dropping a partition is a metadata-only
-- O(1) operation — no VACUUM churn from mass DELETEs. The function is invoked
-- periodically by the system.jobs scheduler (see db/README.md); effective
-- retention is 30–37 days (week granularity). A DEFAULT partition guarantees
-- ingestion never fails even if maintenance lapses.

CREATE SCHEMA ingest;

GRANT USAGE ON SCHEMA ingest TO app_user;
GRANT USAGE ON SCHEMA ingest TO platform_admin;

------------------------------------------------------------------------------
-- raw_webhooks: verbatim webhook deliveries. Partition key must be in the PK,
-- hence the composite (id, received_at).
------------------------------------------------------------------------------
CREATE TABLE ingest.raw_webhooks (
    id uuid NOT NULL DEFAULT gen_random_uuid(),
    received_at timestamptz NOT NULL DEFAULT now(),
    source varchar(30) NOT NULL DEFAULT 'meta',
    tenant_id uuid,
    signature_valid boolean,
    headers jsonb,
    payload jsonb NOT NULL,
    processing_status varchar(20) NOT NULL DEFAULT 'received',
    processed_at timestamptz,
    processing_error text,
    created_at timestamptz NOT NULL DEFAULT now(),
    created_by uuid,
    CONSTRAINT raw_webhooks_pkey PRIMARY KEY (id, received_at),
    CONSTRAINT raw_webhooks_tenant_id_fkey FOREIGN KEY (tenant_id)
    REFERENCES tenancy.tenants (id) ON DELETE SET NULL,
    CONSTRAINT raw_webhooks_processing_status_check CHECK (
        processing_status IN ('received', 'processed', 'failed', 'skipped')
    )
) PARTITION BY RANGE (received_at);

-- Safety net: catches rows if weekly partition maintenance ever lapses.
CREATE TABLE ingest.raw_webhooks_pdefault
PARTITION OF ingest.raw_webhooks DEFAULT;

-- Worker queue scan: unprocessed rows in arrival order.
CREATE INDEX raw_webhooks_pending_idx
ON ingest.raw_webhooks (received_at)
WHERE processing_status = 'received';

CREATE INDEX raw_webhooks_tenant_id_idx ON ingest.raw_webhooks (tenant_id);

COMMENT ON TABLE ingest.raw_webhooks IS
'Verbatim Meta webhook deliveries, 30-day TTL via weekly partition drops
(ingest.maintain_raw_webhooks). Not tenant-scoped: written pre-tenant-resolution.';

------------------------------------------------------------------------------
-- Partition maintenance: create current + weeks_ahead weekly partitions
-- (raw_webhooks_pYYYYMMDD, named by ISO week start) and drop partitions whose
-- entire range is older than retain_days.
------------------------------------------------------------------------------
CREATE FUNCTION ingest.maintain_raw_webhooks(
    retain_days integer DEFAULT 30, weeks_ahead integer DEFAULT 4
)
RETURNS void
LANGUAGE plpgsql
AS $$
DECLARE
    week_start date;
    part_name text;
    child record;
    i integer;
BEGIN
    -- 1. Ensure upcoming weekly partitions exist.
    FOR i IN 0..weeks_ahead LOOP
        week_start := (date_trunc('week', now()) + make_interval(weeks => i))::date;
        part_name := format('raw_webhooks_p%s', to_char(week_start, 'YYYYMMDD'));
        IF to_regclass(format('ingest.%I', part_name)) IS NULL THEN
            EXECUTE format(
                'CREATE TABLE ingest.%I PARTITION OF ingest.raw_webhooks '
                || 'FOR VALUES FROM (%L) TO (%L)',
                part_name, week_start, week_start + 7
            );
        END IF;
    END LOOP;

    -- 2. Drop expired weekly partitions (metadata-only, O(1)).
    FOR child IN
        SELECT c.relname
        FROM pg_catalog.pg_inherits AS i2
        INNER JOIN pg_catalog.pg_class AS c ON i2.inhrelid = c.oid
        WHERE i2.inhparent = 'ingest.raw_webhooks'::regclass
    LOOP
        IF child.relname ~ '^raw_webhooks_p[0-9]{8}$'
        AND to_date(right(child.relname, 8), 'YYYYMMDD') + 7
        < (now() - make_interval(days => retain_days))::date THEN
            EXECUTE format('DROP TABLE ingest.%I', child.relname);
        END IF;
    END LOOP;
END
$$;

-- Create the initial partitions now so the first insert never lands in DEFAULT.
SELECT ingest.maintain_raw_webhooks();

------------------------------------------------------------------------------
-- webhook_dedupe: at-least-once delivery guard. Meta redelivers webhooks; the
-- receiver INSERTs (wamid, event_type) and treats a conflict as "duplicate,
-- skip". Rows expire with the same 30-day horizon (cleanup job deletes on
-- first_seen_at — small table, plain DELETE is fine here).
-- No raw_webhook_id back-reference on purpose: raw_webhooks partitions are
-- dropped wholesale, which would break any FK pointing at them.
------------------------------------------------------------------------------
CREATE TABLE ingest.webhook_dedupe (
    wamid varchar(128) NOT NULL,
    event_type varchar(50) NOT NULL,
    first_seen_at timestamptz NOT NULL DEFAULT now(),
    created_at timestamptz NOT NULL DEFAULT now(),
    created_by uuid,
    CONSTRAINT webhook_dedupe_pkey PRIMARY KEY (wamid, event_type)
);

CREATE INDEX webhook_dedupe_first_seen_at_idx
ON ingest.webhook_dedupe (first_seen_at);

------------------------------------------------------------------------------
-- Grants (no RLS in this schema — see header comment).
------------------------------------------------------------------------------
GRANT SELECT, INSERT, UPDATE, DELETE ON ALL TABLES IN SCHEMA ingest TO app_user;
GRANT SELECT, INSERT, UPDATE, DELETE ON ALL TABLES IN SCHEMA ingest TO platform_admin;

INSERT INTO public.schema_migrations (version)
VALUES ('V003')
ON CONFLICT (version) DO NOTHING;
