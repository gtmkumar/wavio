namespace WaAdmin.Application.Common.Interfaces;

/// <summary>
/// Reacts to a template's category reclassification by recalculating billed cost going forward
/// (spec §4.4: "utility -&gt; marketing changes cost"). Billing does not exist until Wave 2 (#19)
/// — <c>NoOpBillingRecalibrationHook</c> (WaAdmin.Infrastructure) is an HONEST no-op: it records
/// nothing and pretends nothing, so the pipeline shape (call the hook on every category-change
/// event) is in place for #19 to implement for real.
/// </summary>
public interface IBillingRecalibrationHook
{
    Task RecalibrateAsync(Guid templateId, string oldCategory, string newCategory, CancellationToken cancellationToken);
}
