namespace WaAdmin.Application.Common.Interfaces;

/// <summary>Result of a successful tenant resolution — the internal GUIDs the rest of the
/// pipeline needs, resolved from Meta's raw string identifiers. Same shape as WaIntel's/
/// WaBilling's own copy of this type (issue #15 pattern) — deliberately not shared across
/// services (each service owns its data-access surface).</summary>
public sealed record ResolvedTenant(Guid TenantId, Guid PhoneNumberId);

/// <summary>
/// Resolves a tenant + internal phone-number id from Meta's raw <c>phone_number_id</c> string
/// (issue #15 pattern, reused by issue #21's STOP-keyword listener). See WaAdmin.Infrastructure's
/// <c>WabaPhoneNumberTenantResolver</c> for exactly how that's queried and why it needs a
/// privileged connection.
///
/// Callers MUST treat a null result as "park this event" — never substitute
/// <see cref="Guid.Empty"/> or any other placeholder tenant id.
/// </summary>
public interface ITenantResolver
{
    Task<ResolvedTenant?> ResolveAsync(string metaPhoneNumberId, CancellationToken cancellationToken);
}
