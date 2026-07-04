namespace WaAdmin.Application.Common.Interfaces;

/// <summary>
/// Reacts to a template entering PAUSED/DISABLED by freezing any in-flight campaign using it
/// (spec §4.4: "auto-pause freezes campaigns using the template"). Campaigns do not exist until
/// Wave 2 (#22) — <c>NoOpCampaignFreezeHook</c> (WaAdmin.Infrastructure) is an HONEST no-op, not
/// fake logic: it records nothing and pretends nothing, so the pipeline shape (call the hook on
/// every PAUSED/DISABLED transition) is in place for #22 to implement for real without touching
/// the state-machine handler that calls it.
/// </summary>
public interface ICampaignFreezeHook
{
    Task FreezeCampaignsUsingTemplateAsync(Guid templateId, CancellationToken cancellationToken);
}
