namespace WaBilling.Application.Common.Interfaces;

/// <summary>Result of a successful tenant resolution — the internal GUIDs the rest of the
/// pipeline needs, resolved from Meta's raw string identifiers.</summary>
public sealed record ResolvedTenant(Guid TenantId, Guid PhoneNumberId);

/// <summary>
/// Resolves a tenant + internal phone-number id from Meta's raw <c>phone_number_id</c> string
/// (issue #19, same shape as WaIntel's <c>ITenantResolver</c> — issue #15). The status-webhook
/// consumer needs this because <c>MessageStatusV1</c> is published with an unresolved tenant
/// (wa-ingest-svc's Wave-1 gap, see MetaWebhookNormalizer's doc comment) and
/// <c>waba.phone_numbers</c> is RLS-scoped — resolving it requires a privileged connection run
/// BEFORE any tenant GUC can be set.
///
/// Callers MUST treat a null result as "park this event, don't write a cost row" — never
/// substitute <see cref="Guid.Empty"/>; <c>message_costs.tenant_id</c>'s foreign key to
/// <c>tenancy.tenants</c> would reject it anyway, and an orphaned Guid.Empty-tenant row would be
/// invisible to every RLS-scoped read.
/// </summary>
public interface ITenantResolver
{
    Task<ResolvedTenant?> ResolveAsync(string metaPhoneNumberId, CancellationToken cancellationToken);
}
