namespace wavio.SharedDataModel.Entities.Billing;

/// <summary>
/// GST invoice trail per tenant (billing.invoices_feed, spec §11 — GSTIN + HSN/SAC). Rows arrive
/// via Meta/PSP invoice import (not built here — issue #19 only reads this table, for the
/// minimal-v1 reconciliation report comparing it against <see cref="MessageCost"/> totals).
/// Tenant-scoped, RLS.
/// </summary>
public class InvoiceFeed
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public string? InvoiceNumber { get; set; }
    public DateOnly PeriodStart { get; set; }
    public DateOnly PeriodEnd { get; set; }
    public string? Gstin { get; set; }
    public string? PlaceOfSupply { get; set; }
    public string? HsnSacCode { get; set; }
    public decimal TaxableAmount { get; set; }
    public decimal CgstAmount { get; set; }
    public decimal SgstAmount { get; set; }
    public decimal IgstAmount { get; set; }
    public decimal TotalAmount { get; set; }
    public string Currency { get; set; } = "INR";

    /// <summary>draft | issued | paid | cancelled.</summary>
    public string Status { get; set; } = "draft";

    public string LineItems { get; set; } = "[]";
    public DateTimeOffset? IssuedAt { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public Guid? CreatedBy { get; set; }
    public Guid? UpdatedBy { get; set; }
    public int Version { get; set; }
}
