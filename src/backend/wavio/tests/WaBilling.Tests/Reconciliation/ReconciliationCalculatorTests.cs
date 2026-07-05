using WaBilling.Application.Reconciliation.Logic;
using Xunit;

namespace WaBilling.Tests.Reconciliation;

public class ReconciliationCalculatorTests
{
    [Fact]
    public void Evaluate_PerfectMatch_ZeroVarianceWithinTarget()
    {
        var (varianceAmount, variancePercent, withinTarget) = ReconciliationCalculator.Evaluate(1000m, 1000m);

        Assert.Equal(0m, varianceAmount);
        Assert.Equal(0m, variancePercent);
        Assert.True(withinTarget);
    }

    [Fact]
    public void Evaluate_VarianceUnderHalfPercent_IsWithinTarget()
    {
        // 0.4% variance on a 1000 ledger total.
        var (_, variancePercent, withinTarget) = ReconciliationCalculator.Evaluate(1000m, 1004m);

        Assert.Equal(0.4m, variancePercent);
        Assert.True(withinTarget);
    }

    [Fact]
    public void Evaluate_VarianceOverHalfPercent_IsOutsideTarget()
    {
        var (_, variancePercent, withinTarget) = ReconciliationCalculator.Evaluate(1000m, 1010m);

        Assert.Equal(1.0m, variancePercent);
        Assert.False(withinTarget);
    }

    [Fact]
    public void Evaluate_VarianceExactlyAtTargetThreshold_IsWithinTarget()
    {
        var (_, variancePercent, withinTarget) = ReconciliationCalculator.Evaluate(1000m, 1005m);

        Assert.Equal(0.5m, variancePercent);
        Assert.True(withinTarget);
    }

    [Fact]
    public void Evaluate_ZeroLedgerAndZeroInvoice_IsAPerfectTrivialMatch()
    {
        var (varianceAmount, variancePercent, withinTarget) = ReconciliationCalculator.Evaluate(0m, 0m);

        Assert.Equal(0m, varianceAmount);
        Assert.Equal(0m, variancePercent);
        Assert.True(withinTarget);
    }

    [Fact]
    public void Evaluate_ZeroLedgerButNonZeroInvoice_HasNoPercentBaseAndIsFlaggedOutsideTarget()
    {
        var (varianceAmount, variancePercent, withinTarget) = ReconciliationCalculator.Evaluate(0m, 50m);

        Assert.Equal(50m, varianceAmount);
        Assert.Null(variancePercent);
        Assert.False(withinTarget);
    }

    [Fact]
    public void Evaluate_InvoiceLowerThanLedger_VarianceIsNegativeButPercentIsAbsolute()
    {
        var (varianceAmount, variancePercent, withinTarget) = ReconciliationCalculator.Evaluate(1000m, 990m);

        Assert.Equal(-10m, varianceAmount);
        Assert.Equal(1.0m, variancePercent);
        Assert.False(withinTarget);
    }
}
