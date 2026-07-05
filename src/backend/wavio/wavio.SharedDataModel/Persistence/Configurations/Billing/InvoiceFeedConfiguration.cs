using wavio.SharedDataModel.Entities.Billing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace wavio.SharedDataModel.Persistence.Configurations.Billing;

public sealed class InvoiceFeedConfiguration : IEntityTypeConfiguration<InvoiceFeed>
{
    public void Configure(EntityTypeBuilder<InvoiceFeed> builder)
    {
        builder.ToTable("invoices_feed", "billing");

        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).HasColumnName("id").ValueGeneratedOnAdd();

        builder.Property(e => e.TenantId).HasColumnName("tenant_id").IsRequired();
        builder.Property(e => e.InvoiceNumber).HasColumnName("invoice_number").HasMaxLength(64);
        builder.Property(e => e.PeriodStart).HasColumnName("period_start").IsRequired();
        builder.Property(e => e.PeriodEnd).HasColumnName("period_end").IsRequired();
        builder.Property(e => e.Gstin).HasColumnName("gstin").HasMaxLength(15);
        builder.Property(e => e.PlaceOfSupply).HasColumnName("place_of_supply").HasMaxLength(60);
        builder.Property(e => e.HsnSacCode).HasColumnName("hsn_sac_code").HasMaxLength(10);
        builder.Property(e => e.TaxableAmount).HasColumnName("taxable_amount").HasColumnType("numeric(14,2)").IsRequired();
        builder.Property(e => e.CgstAmount).HasColumnName("cgst_amount").HasColumnType("numeric(14,2)").IsRequired();
        builder.Property(e => e.SgstAmount).HasColumnName("sgst_amount").HasColumnType("numeric(14,2)").IsRequired();
        builder.Property(e => e.IgstAmount).HasColumnName("igst_amount").HasColumnType("numeric(14,2)").IsRequired();
        builder.Property(e => e.TotalAmount).HasColumnName("total_amount").HasColumnType("numeric(14,2)").IsRequired();
        builder.Property(e => e.Currency).HasColumnName("currency").HasMaxLength(3).IsRequired();
        builder.Property(e => e.Status).HasColumnName("status").HasMaxLength(20).IsRequired();
        builder.Property(e => e.LineItems).HasColumnName("line_items").HasColumnType("jsonb").IsRequired();
        builder.Property(e => e.IssuedAt).HasColumnName("issued_at");

        builder.Property(e => e.CreatedAt).HasColumnName("created_at").IsRequired();
        builder.Property(e => e.UpdatedAt).HasColumnName("updated_at").IsRequired();
        builder.Property(e => e.CreatedBy).HasColumnName("created_by");
        builder.Property(e => e.UpdatedBy).HasColumnName("updated_by");
        builder.Property(e => e.Version).HasColumnName("version").IsRequired();

        builder.HasIndex(e => new { e.TenantId, e.InvoiceNumber })
            .IsUnique()
            .HasDatabaseName("invoices_feed_tenant_id_invoice_number_key")
            .HasFilter("invoice_number IS NOT NULL");
    }
}
