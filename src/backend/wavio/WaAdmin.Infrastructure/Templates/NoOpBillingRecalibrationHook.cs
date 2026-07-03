using WaAdmin.Application.Common.Interfaces;
using Microsoft.Extensions.Logging;

namespace WaAdmin.Infrastructure.Templates;

/// <summary>
/// Wave 1 no-op (issue #16 Task 4): billing does not exist until Wave 2 (#19). Logs that a
/// recalibration would have happened rather than silently doing nothing or faking a calculation.
/// </summary>
public sealed partial class NoOpBillingRecalibrationHook : IBillingRecalibrationHook
{
    private readonly ILogger<NoOpBillingRecalibrationHook> _logger;
    public NoOpBillingRecalibrationHook(ILogger<NoOpBillingRecalibrationHook> logger) => _logger = logger;

    public Task RecalibrateAsync(Guid templateId, string oldCategory, string newCategory, CancellationToken cancellationToken)
    {
        LogNoOp(_logger, templateId, oldCategory, newCategory);
        return Task.CompletedTask;
    }

    [LoggerMessage(Level = LogLevel.Information,
        Message = "Template {TemplateId} recategorized {OldCategory} -> {NewCategory} — billing recalibration is a no-op until Wave 2 (#19)")]
    private static partial void LogNoOp(ILogger logger, Guid templateId, string oldCategory, string newCategory);
}
