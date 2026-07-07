-- V014__onboarding.sql
-- Onboarding wizard (docs/ONBOARDING_WIZARD_PLAN.md, spec §4.1): additive review-status
-- columns on the existing waba tables. No new tables — wizard progress is derived from
-- these columns plus a live Graph refresh, never stored as its own state machine.

------------------------------------------------------------------------------
-- business_accounts: Meta business-verification review + webhook subscription
-- evidence (subscribed_apps is called during Embedded Signup; the timestamp is
-- the proof the call succeeded, and null means "not yet subscribed").
------------------------------------------------------------------------------
ALTER TABLE waba.business_accounts
ADD COLUMN verification_status varchar(30),
ADD COLUMN webhooks_subscribed_at timestamptz;

COMMENT ON COLUMN waba.business_accounts.verification_status IS
'Meta business verification review state as last reported by the Graph API
(e.g. pending, verified, not_verified). Meta decides; we only mirror it.';

COMMENT ON COLUMN waba.business_accounts.webhooks_subscribed_at IS
'When POST /{waba-id}/subscribed_apps last succeeded for this WABA (Embedded
Signup onboarding). NULL = webhooks not subscribed yet.';

------------------------------------------------------------------------------
-- phone_numbers: display-name review, OTP verification state, and the moment
-- Cloud API registration (/register) succeeded.
------------------------------------------------------------------------------
ALTER TABLE waba.phone_numbers
ADD COLUMN name_status varchar(30),
ADD COLUMN code_verification_status varchar(30),
ADD COLUMN registered_at timestamptz;

COMMENT ON COLUMN waba.phone_numbers.name_status IS
'Meta display-name review state as last reported by the Graph API
(e.g. NONE, PENDING_REVIEW, APPROVED, DECLINED). Meta decides; we mirror.';

COMMENT ON COLUMN waba.phone_numbers.code_verification_status IS
'Meta OTP ownership-verification state (e.g. NOT_VERIFIED, VERIFIED) as last
reported by the Graph API.';

COMMENT ON COLUMN waba.phone_numbers.registered_at IS
'When POST /{phone-id}/register succeeded (Cloud API registration with the
two-step pin). NULL = not registered yet.';

INSERT INTO public.schema_migrations (version)
VALUES ('V014')
ON CONFLICT (version) DO NOTHING;
