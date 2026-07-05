using wavio.SharedDataModel.Entities.Billing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace wavio.SharedDataModel.Persistence.Configurations.Billing;

public sealed class MessageCostConfiguration : IEntityTypeConfiguration<MessageCost>
{
    public void Configure(EntityTypeBuilder<MessageCost> builder)
    {
        builder.ToTable("message_costs", "billing");

        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).HasColumnName("id").ValueGeneratedOnAdd();

        builder.Property(e => e.TenantId).HasColumnName("tenant_id").IsRequired();
        builder.Property(e => e.PhoneNumberId).HasColumnName("phone_number_id").IsRequired();
        builder.Property(e => e.RateCardId).HasColumnName("rate_card_id");
        builder.Property(e => e.Wamid).HasColumnName("wamid").HasMaxLength(128).IsRequired();
        builder.Property(e => e.Category).HasColumnName("category").HasMaxLength(30).IsRequired();
        builder.Property(e => e.PricingModel).HasColumnName("pricing_model").HasMaxLength(20);
        builder.Property(e => e.PricingCategory).HasColumnName("pricing_category").HasMaxLength(40);
        builder.Property(e => e.Billable).HasColumnName("billable").IsRequired();
        builder.Property(e => e.Amount).HasColumnName("amount").HasColumnType("numeric(12,6)").IsRequired();
        builder.Property(e => e.Currency).HasColumnName("currency").HasMaxLength(3).IsRequired();
        builder.Property(e => e.DestinationMarket).HasColumnName("destination_market").HasMaxLength(60);
        builder.Property(e => e.WebhookPricing).HasColumnName("webhook_pricing").HasColumnType("jsonb");
        builder.Property(e => e.BilledAt).HasColumnName("billed_at").IsRequired();

        builder.Property(e => e.CreatedAt).HasColumnName("created_at").IsRequired();
        builder.Property(e => e.CreatedBy).HasColumnName("created_by");

        builder.HasIndex(e => e.Wamid)
            .IsUnique()
            .HasDatabaseName("message_costs_wamid_key");

        builder.HasIndex(e => new { e.TenantId, e.BilledAt })
            .HasDatabaseName("message_costs_tenant_id_billed_at_idx");
    }
}
