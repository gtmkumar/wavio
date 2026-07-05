namespace WaBilling.Application.Reconciliation.Logic;

/// <summary>Pure variance math for the minimal-v1 reconciliation report (issue #19, spec §4.7:
/// target &lt;0.5% variance). No I/O — callers supply the already-summed totals.</summary>
public static class ReconciliationCalculator
{
    /// <summary>Target variance threshold from spec §4.7.</summary>
    public const decimal TargetVariancePercent = 0.5m;

    public static (decimal VarianceAmount, decimal? VariancePercent, bool WithinTarget) Evaluate(
        decimal ledgerTotal, decimal invoiceTotal)
    {
        var varianceAmount = invoiceTotal - ledgerTotal;

        if (ledgerTotal == 0m)
        {
            // No spend recorded: an invoice total of 0 is a perfect (if trivial) match; any
            // nonzero invoice total against zero ledger spend has no meaningful percentage base,
            // so it's reported as an undefined percent and flagged as outside target rather than
            // dividing by zero or silently reporting 0%.
            return invoiceTotal == 0m ? (0m, 0m, true) : (varianceAmount, null, false);
        }

        var variancePercent = Math.Abs(varianceAmount) / ledgerTotal * 100m;
        return (varianceAmount, variancePercent, variancePercent <= TargetVariancePercent);
    }
}
