using wavio.SharedDataModel.Entities.Billing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace wavio.SharedDataModel.Persistence.Configurations.Billing;

public sealed class RateCardEntryConfiguration : IEntityTypeConfiguration<RateCardEntry>
{
    public void Configure(EntityTypeBuilder<RateCardEntry> builder)
    {
        builder.ToTable("rate_card_entries", "billing");

        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).HasColumnName("id").ValueGeneratedOnAdd();

        builder.Property(e => e.RateCardId).HasColumnName("rate_card_id").IsRequired();
        builder.Property(e => e.Category).HasColumnName("category").HasMaxLength(30).IsRequired();
        builder.Property(e => e.Market).HasColumnName("market").HasMaxLength(60).IsRequired();
        builder.Property(e => e.VolumeTier).HasColumnName("volume_tier").HasMaxLength(20);
        builder.Property(e => e.PricePerMessage).HasColumnName("price_per_message").HasColumnType("numeric(12,6)").IsRequired();
        builder.Property(e => e.Currency).HasColumnName("currency").HasMaxLength(3).IsRequired();

        builder.Property(e => e.CreatedAt).HasColumnName("created_at").IsRequired();
        builder.Property(e => e.UpdatedAt).HasColumnName("updated_at").IsRequired();
        builder.Property(e => e.CreatedBy).HasColumnName("created_by");
        builder.Property(e => e.UpdatedBy).HasColumnName("updated_by");
        builder.Property(e => e.Version).HasColumnName("version").IsRequired();

        // volume_tier NULL must not collide with itself (NULLS NOT DISTINCT in the DB) — informational
        // only, this context is database-first and never generates DDL from this metadata.
        builder.HasIndex(e => new { e.RateCardId, e.Category, e.Market, e.VolumeTier })
            .IsUnique()
            .HasDatabaseName("rate_card_entries_card_category_market_tier_key");
    }
}
