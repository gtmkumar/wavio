#!/usr/bin/env bash
# Two-tenant RLS smoke test (issue #10 acceptance criterion).
#
# Creates two tenants as the postgres superuser, then connects as app_user
# (the RLS-enforced runtime role) and proves:
#   1. with app.tenant_id = A, tenant B's rows are invisible
#   2. with app.tenant_id = A, rows for tenant B cannot be inserted
#   3. the reverse (B cannot see/write A) — using the app.current_tenant_id
#      GUC spelling to also cover the RlsConnectionInterceptor compatibility path
#   4. with no tenant context, no tenant-scoped rows are visible
#   5. global rows (tenant_id IS NULL, e.g. system.feature_flags) are visible
#      to every tenant session
#   6. system.audit_log is append-only for app_user (INSERT ok, UPDATE/DELETE denied)
#   7. app_user has no BYPASSRLS / superuser attribute
#   8. Wave 1 schemas (V007-V009): isolation holds on messaging.suppression_list,
#      sessions.conversation_windows and templates.templates; template_packs
#      global rows (tenant_id IS NULL) are visible to every tenant
#
# Exits non-zero on the first failed assertion. Runnable locally and in CI:
#   ./db/tests/rls_smoke_test.sh
# Override connection via ADMIN_URL / APP_URL env vars if needed.

set -euo pipefail

PGHOST="${PGHOST:-localhost}"
PGPORT="${PGPORT:-5432}"
PGDB="${PGDATABASE:-waplatform}"
ADMIN_URL="${ADMIN_URL:-postgresql://postgres:postgres@${PGHOST}:${PGPORT}/${PGDB}}"
APP_URL="${APP_URL:-postgresql://app_user:app_user@${PGHOST}:${PGPORT}/${PGDB}}"

TENANT_A="11111111-1111-1111-1111-111111111111"
TENANT_B="22222222-2222-2222-2222-222222222222"
FLAG_ID="33333333-3333-3333-3333-333333333333"
# Wave 1 (V007-V009) fixtures — one WABA + phone number + window + template
# per tenant, one suppression entry per tenant, one global template pack.
BA_A="44444444-4444-4444-4444-4444444444aa"
BA_B="44444444-4444-4444-4444-4444444444bb"
PN_A="55555555-5555-5555-5555-5555555555aa"
PN_B="55555555-5555-5555-5555-5555555555bb"
PACK_ID="66666666-6666-6666-6666-666666666666"

FAILURES=0

pass() { echo "  ok   - $1"; }
fail() { echo "  FAIL - $1"; FAILURES=$((FAILURES + 1)); }

admin_sql() { psql "$ADMIN_URL" -v ON_ERROR_STOP=1 -qAtX -c "$1"; }
app_sql()   { psql "$APP_URL"   -v ON_ERROR_STOP=1 -qAtX -c "$1"; }

# assert_eq <description> <expected> <actual>
assert_eq() {
    if [ "$2" = "$3" ]; then pass "$1"; else fail "$1 (expected [$2], got [$3])"; fi
}

# assert_denied <description> <sql-as-app_user>
assert_denied() {
    if psql "$APP_URL" -v ON_ERROR_STOP=1 -qAtX -c "$2" > /dev/null 2>&1; then
        fail "$1 (statement unexpectedly succeeded)"
    else
        pass "$1"
    fi
}

# assert_ok <description> <sql-as-app_user>
assert_ok() {
    if psql "$APP_URL" -v ON_ERROR_STOP=1 -qAtX -c "$2" > /dev/null 2>&1; then
        pass "$1"
    else
        fail "$1 (statement unexpectedly failed)"
    fi
}

cleanup() {
    # Superuser: not subject to RLS or the append-only grants. Order matters:
    # waba.business_accounts FK to tenants is RESTRICT (deleting it cascades
    # phone_numbers -> conversation_windows and templates.* first).
    admin_sql "DELETE FROM system.audit_log
               WHERE tenant_id IN ('$TENANT_A', '$TENANT_B');" > /dev/null
    admin_sql "DELETE FROM system.feature_flags WHERE id = '$FLAG_ID';" > /dev/null
    admin_sql "DELETE FROM templates.template_packs WHERE id = '$PACK_ID';" > /dev/null
    admin_sql "DELETE FROM waba.business_accounts
               WHERE id IN ('$BA_A', '$BA_B');" > /dev/null
    admin_sql "DELETE FROM messaging.suppression_list
               WHERE tenant_id IN ('$TENANT_A', '$TENANT_B');" > /dev/null
    admin_sql "DELETE FROM tenancy.tenants
               WHERE id IN ('$TENANT_A', '$TENANT_B');" > /dev/null
}

echo "== RLS smoke test against $PGDB =="

echo "-- setup (as superuser)"
cleanup
admin_sql "INSERT INTO tenancy.tenants
               (id, code, name, currency_code, country_code, timezone)
           VALUES
               ('$TENANT_A', 'smoke-a', 'Smoke Tenant A', 'INR', 'IN', 'Asia/Kolkata'),
               ('$TENANT_B', 'smoke-b', 'Smoke Tenant B', 'INR', 'IN', 'Asia/Kolkata');" > /dev/null
admin_sql "INSERT INTO tenancy.tenant_settings (tenant_id, category, setting_key, setting_value)
           VALUES ('$TENANT_A', 'smoke', 'k', '\"a\"'),
                  ('$TENANT_B', 'smoke', 'k', '\"b\"');" > /dev/null
admin_sql "INSERT INTO system.feature_flags
               (id, tenant_id, flag_key, name, flag_type, status)
           VALUES ('$FLAG_ID', NULL, 'smoke_global_flag', 'Smoke global flag',
                   'boolean', 'active');" > /dev/null
admin_sql "INSERT INTO waba.business_accounts (id, tenant_id, meta_waba_id, name)
           VALUES ('$BA_A', '$TENANT_A', 'smoke-waba-a', 'Smoke WABA A'),
                  ('$BA_B', '$TENANT_B', 'smoke-waba-b', 'Smoke WABA B');" > /dev/null
admin_sql "INSERT INTO waba.phone_numbers
               (id, tenant_id, business_account_id, meta_phone_number_id, display_phone_number)
           VALUES ('$PN_A', '$TENANT_A', '$BA_A', 'smoke-pn-a', '+911111111111'),
                  ('$PN_B', '$TENANT_B', '$BA_B', 'smoke-pn-b', '+912222222222');" > /dev/null
admin_sql "INSERT INTO sessions.conversation_windows
               (tenant_id, phone_number_id, user_wa_id, origin, cs_expires_at)
           VALUES ('$TENANT_A', '$PN_A', '919000000001', 'organic', now() + interval '24 hours'),
                  ('$TENANT_B', '$PN_B', '919000000002', 'organic', now() + interval '24 hours');" > /dev/null
admin_sql "INSERT INTO templates.templates
               (tenant_id, business_account_id, name, language, category)
           VALUES ('$TENANT_A', '$BA_A', 'smoke_template_a', 'en_US', 'utility'),
                  ('$TENANT_B', '$BA_B', 'smoke_template_b', 'en_US', 'utility');" > /dev/null
admin_sql "INSERT INTO messaging.suppression_list (tenant_id, wa_id, reason)
           VALUES ('$TENANT_A', '919000000001', 'opt_out'),
                  ('$TENANT_B', '919000000002', 'opt_out');" > /dev/null
admin_sql "INSERT INTO templates.template_packs
               (id, tenant_id, pack_key, vertical, name)
           VALUES ('$PACK_ID', NULL, 'smoke_pack', 'smoke', 'Smoke global pack');" > /dev/null

echo "-- role hygiene"
assert_eq "app_user is not superuser and not BYPASSRLS" "false|false" \
    "$(admin_sql "SELECT rolsuper || '|' || rolbypassrls
                  FROM pg_roles WHERE rolname = 'app_user';")"
assert_eq "app_user is not a member of platform_admin" "0" \
    "$(admin_sql "SELECT count(*) FROM pg_auth_members m
                  JOIN pg_roles r ON r.oid = m.roleid
                  JOIN pg_roles u ON u.oid = m.member
                  WHERE r.rolname = 'platform_admin' AND u.rolname = 'app_user';")"

echo "-- tenant A context (app.tenant_id GUC, spec §5 spelling)"
assert_eq "A sees exactly its own tenants row" "1|$TENANT_A" \
    "$(app_sql "SET app.tenant_id = '$TENANT_A';
                SELECT count(*) || '|' || min(id::text) FROM tenancy.tenants;")"
assert_eq "A sees only its own tenant_settings row" "a" \
    "$(app_sql "SET app.tenant_id = '$TENANT_A';
                SELECT string_agg(setting_value #>> '{}', ',')
                FROM tenancy.tenant_settings WHERE category = 'smoke';")"
assert_denied "A cannot insert a row for tenant B (WITH CHECK)" \
    "SET app.tenant_id = '$TENANT_A';
     INSERT INTO tenancy.tenant_settings (tenant_id, category, setting_key, setting_value)
     VALUES ('$TENANT_B', 'smoke', 'intrusion', '\"x\"');"
assert_ok "A can insert a row for itself" \
    "SET app.tenant_id = '$TENANT_A';
     INSERT INTO tenancy.tenant_settings (tenant_id, category, setting_key, setting_value)
     VALUES ('$TENANT_A', 'smoke', 'own-row', '\"ok\"');"
assert_eq "A's UPDATE against B's settings touches zero rows" "0" \
    "$(app_sql "SET app.tenant_id = '$TENANT_A';
                WITH upd AS (
                    UPDATE tenancy.tenant_settings SET setting_value = '\"hacked\"'
                    WHERE tenant_id = '$TENANT_B' RETURNING 1
                )
                SELECT count(*) FROM upd;")"

echo "-- tenant B context (app.current_tenant_id GUC, interceptor spelling)"
assert_eq "B sees exactly its own tenants row" "1|$TENANT_B" \
    "$(app_sql "SET app.current_tenant_id = '$TENANT_B';
                SELECT count(*) || '|' || min(id::text) FROM tenancy.tenants;")"
assert_eq "B sees only its own tenant_settings row" "b" \
    "$(app_sql "SET app.current_tenant_id = '$TENANT_B';
                SELECT string_agg(setting_value #>> '{}', ',')
                FROM tenancy.tenant_settings WHERE category = 'smoke';")"
assert_denied "B cannot insert a row for tenant A (WITH CHECK)" \
    "SET app.current_tenant_id = '$TENANT_B';
     INSERT INTO tenancy.tenant_settings (tenant_id, category, setting_key, setting_value)
     VALUES ('$TENANT_A', 'smoke', 'intrusion', '\"x\"');"

echo "-- no tenant context"
assert_eq "unset context sees zero tenants" "0" \
    "$(app_sql "SELECT count(*) FROM tenancy.tenants;")"
assert_eq "unset context sees zero tenant_settings" "0" \
    "$(app_sql "SELECT count(*) FROM tenancy.tenant_settings WHERE category = 'smoke';")"

echo "-- global rows (nullable-tenant pattern)"
assert_eq "global feature flag visible from tenant A session" "1" \
    "$(app_sql "SET app.tenant_id = '$TENANT_A';
                SELECT count(*) FROM system.feature_flags
                WHERE flag_key = 'smoke_global_flag';")"
assert_eq "global feature flag visible from tenant B session" "1" \
    "$(app_sql "SET app.tenant_id = '$TENANT_B';
                SELECT count(*) FROM system.feature_flags
                WHERE flag_key = 'smoke_global_flag';")"

echo "-- append-only audit log"
assert_ok "A can INSERT into system.audit_log" \
    "SET app.tenant_id = '$TENANT_A';
     INSERT INTO system.audit_log (tenant_id, actor_type, action, resource_type)
     VALUES ('$TENANT_A', 'system', 'smoke.test', 'smoke');"
assert_denied "app_user cannot UPDATE system.audit_log" \
    "SET app.tenant_id = '$TENANT_A';
     UPDATE system.audit_log SET action = 'tampered' WHERE tenant_id = '$TENANT_A';"
assert_denied "app_user cannot DELETE from system.audit_log" \
    "SET app.tenant_id = '$TENANT_A';
     DELETE FROM system.audit_log WHERE tenant_id = '$TENANT_A';"

echo "-- Wave 1 schemas (messaging / sessions / templates)"
assert_eq "A sees only its own suppression_list row" "919000000001" \
    "$(app_sql "SET app.tenant_id = '$TENANT_A';
                SELECT string_agg(wa_id, ',') FROM messaging.suppression_list;")"
assert_denied "A cannot insert a suppression row for tenant B (WITH CHECK)" \
    "SET app.tenant_id = '$TENANT_A';
     INSERT INTO messaging.suppression_list (tenant_id, wa_id, reason)
     VALUES ('$TENANT_B', '919000000009', 'manual');"
assert_eq "B sees only its own conversation_windows row" "919000000002" \
    "$(app_sql "SET app.current_tenant_id = '$TENANT_B';
                SELECT string_agg(user_wa_id, ',')
                FROM sessions.conversation_windows;")"
assert_denied "B cannot insert a window for tenant A (WITH CHECK)" \
    "SET app.current_tenant_id = '$TENANT_B';
     INSERT INTO sessions.conversation_windows (tenant_id, phone_number_id, user_wa_id)
     VALUES ('$TENANT_A', '$PN_A', '919000000009');"
assert_eq "A sees only its own templates row" "smoke_template_a" \
    "$(app_sql "SET app.tenant_id = '$TENANT_A';
                SELECT string_agg(name, ',') FROM templates.templates;")"
assert_eq "B's UPDATE against A's template touches zero rows" "0" \
    "$(app_sql "SET app.current_tenant_id = '$TENANT_B';
                WITH upd AS (
                    UPDATE templates.templates SET status = 'DISABLED'
                    WHERE tenant_id = '$TENANT_A' RETURNING 1
                )
                SELECT count(*) FROM upd;")"
assert_eq "global template pack visible from both tenant sessions" "1|1" \
    "$(app_sql "SET app.tenant_id = '$TENANT_A';
                SELECT count(*) FROM templates.template_packs
                WHERE pack_key = 'smoke_pack';")|$(app_sql "
                SET app.tenant_id = '$TENANT_B';
                SELECT count(*) FROM templates.template_packs
                WHERE pack_key = 'smoke_pack';")"
assert_eq "unset context sees zero conversation_windows" "0" \
    "$(app_sql "SELECT count(*) FROM sessions.conversation_windows;")"

echo "-- teardown"
cleanup

if [ "$FAILURES" -gt 0 ]; then
    echo "== RLS smoke test: $FAILURES assertion(s) FAILED =="
    exit 1
fi
echo "== RLS smoke test: all assertions passed =="
