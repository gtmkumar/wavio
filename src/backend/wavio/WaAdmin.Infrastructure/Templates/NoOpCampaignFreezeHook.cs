using WaAdmin.Application.Common.Interfaces;
using Microsoft.Extensions.Logging;

namespace WaAdmin.Infrastructure.Templates;

/// <summary>
/// Wave 1 no-op (issue #16 Task 5): campaigns do not exist until Wave 2 (#22). This deliberately
/// does NOT fake a freeze — it logs that a freeze would have happened and returns, so #22 has an
/// honest, already-wired call site to implement against instead of inventing one later.
/// </summary>
public sealed partial class NoOpCampaignFreezeHook : ICampaignFreezeHook
{
    private readonly ILogger<NoOpCampaignFreezeHook> _logger;
    public NoOpCampaignFreezeHook(ILogger<NoOpCampaignFreezeHook> logger) => _logger = logger;

    public Task FreezeCampaignsUsingTemplateAsync(Guid templateId, CancellationToken cancellationToken)
    {
        LogNoOp(_logger, templateId);
        return Task.CompletedTask;
    }

    [LoggerMessage(Level = LogLevel.Information,
        Message = "Template {TemplateId} paused/disabled — campaign freeze is a no-op until Wave 2 (#22)")]
    private static partial void LogNoOp(ILogger logger, Guid templateId);
}
