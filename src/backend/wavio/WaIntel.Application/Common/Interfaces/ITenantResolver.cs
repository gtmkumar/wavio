namespace WaIntel.Application.Common.Interfaces;

/// <summary>Result of a successful tenant resolution — the internal GUIDs the rest of the
/// pipeline needs, resolved from Meta's raw string identifiers.</summary>
public sealed record ResolvedTenant(Guid TenantId, Guid PhoneNumberId);

/// <summary>
/// Resolves a tenant + internal phone-number id from Meta's raw <c>phone_number_id</c> string
/// (issue #15). Deliberately pluggable: Wave 1 reality is that <c>waba.phone_numbers</c> is empty
/// (WABA onboarding, issue #6, doesn't exist yet) AND that table is RLS-scoped, so resolution can
/// fail for every event today — see WaIntel.Infrastructure's <c>WabaPhoneNumberTenantResolver</c>
/// for exactly how that's queried and why it needs a privileged connection.
///
/// Callers MUST treat a null result as "park this event, don't write a window row" — never
/// substitute <see cref="Guid.Empty"/> or any other placeholder tenant id; the
/// <c>conversation_windows.tenant_id</c> foreign key to <c>tenancy.tenants</c> would reject it
/// anyway, but the real reason is architectural: an orphaned Guid.Empty-tenant row is invisible to
/// every RLS-scoped read and un-recoverable without superuser access.
/// </summary>
public interface ITenantResolver
{
    Task<ResolvedTenant?> ResolveAsync(string metaPhoneNumberId, CancellationToken cancellationToken);
}
