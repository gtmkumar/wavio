namespace WaBilling.Application.Reconciliation.Dtos;

/// <summary>GET /v1/reconciliation result — minimal v1 (issue #19, spec §4.7): compares the
/// ledger's own billable total for a period against whatever <c>billing.invoices_feed</c> rows
/// have arrived for that same window. No external Meta invoice import lives here yet (spec:
/// "reconciles against Meta invoice exports monthly" — those rows arrive out of band via
/// invoices_feed; this only reads them). Target variance is &lt;0.5% (spec §4.7).</summary>
public sealed record ReconciliationDto(
    DateOnly PeriodStart,
    DateOnly PeriodEnd,
    decimal LedgerTotal,
    int LedgerRowCount,
    decimal InvoiceTotal,
    int InvoiceRowCount,
    decimal VarianceAmount,
    decimal? VariancePercent,
    bool WithinTarget);
