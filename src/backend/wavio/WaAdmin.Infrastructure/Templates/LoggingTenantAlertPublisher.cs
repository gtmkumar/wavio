using WaAdmin.Application.Common.Interfaces;
using Microsoft.Extensions.Logging;

namespace WaAdmin.Infrastructure.Templates;

/// <summary>
/// Wave 1 stub (issue #16 Task 4): no tenant-facing notification channel (email/SMS/in-app)
/// exists yet, so a tenant alert is a structured log line. Documented as an honest gap, not a
/// silent no-op — replace this implementation (not <see cref="ITenantAlertPublisher"/>) once a
/// real channel lands.
/// </summary>
public sealed partial class LoggingTenantAlertPublisher : ITenantAlertPublisher
{
    private readonly ILogger<LoggingTenantAlertPublisher> _logger;
    public LoggingTenantAlertPublisher(ILogger<LoggingTenantAlertPublisher> logger) => _logger = logger;

    public Task RaiseAsync(Guid tenantId, string alertCode, string message, CancellationToken cancellationToken)
    {
        LogAlert(_logger, tenantId, alertCode, message);
        return Task.CompletedTask;
    }

    [LoggerMessage(Level = LogLevel.Warning,
        Message = "Tenant alert [{AlertCode}] for tenant {TenantId}: {Message} (no real notification channel yet — logged only)")]
    private static partial void LogAlert(ILogger logger, Guid tenantId, string alertCode, string message);
}
