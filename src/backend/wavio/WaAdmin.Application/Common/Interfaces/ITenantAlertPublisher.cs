namespace WaAdmin.Application.Common.Interfaces;

/// <summary>
/// Raises a tenant-facing alert. No real notification channel (email/SMS/in-app) exists yet —
/// <c>LoggingTenantAlertPublisher</c> (WaAdmin.Infrastructure) writes a structured log line as an
/// honest stub, documented here rather than silently doing nothing. Wired for the category
/// -reclassification reaction (spec §4.4, issue #16 Task 4); replace the implementation, not this
/// interface, once a real channel lands.
/// </summary>
public interface ITenantAlertPublisher
{
    Task RaiseAsync(Guid tenantId, string alertCode, string message, CancellationToken cancellationToken);
}
