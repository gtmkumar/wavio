namespace WaGateway.Application.Common.Interfaces;

/// <summary>Result of a successful tenant resolution — the internal GUIDs the rest of the
/// pipeline needs, resolved from Meta's raw string identifiers.</summary>
public sealed record ResolvedTenant(Guid TenantId, Guid PhoneNumberId);

/// <summary>
/// Resolves a tenant + internal phone-number id from Meta's raw <c>phone_number_id</c> string
/// (issue #22's <c>CampaignStatusConsumerService</c>, same shape as WaBilling's issue #19 and
/// WaIntel's issue #15 <c>ITenantResolver</c> — a local copy per service, not a shared reference,
/// same convention as <see cref="IWindowStateClient"/>/<c>GuardianThrottleRules</c>).
/// <c>wa.message.status.v1</c> is published with an unresolved tenant (wa-ingest-svc's Wave-1 gap)
/// and <c>waba.phone_numbers</c> is RLS-scoped — resolving it requires a privileged connection run
/// BEFORE any tenant GUC can be set.
///
/// Callers MUST treat a null result as "park this event" — never substitute
/// <see cref="Guid.Empty"/>.
/// </summary>
public interface ITenantResolver
{
    Task<ResolvedTenant?> ResolveAsync(string metaPhoneNumberId, CancellationToken cancellationToken);
}
