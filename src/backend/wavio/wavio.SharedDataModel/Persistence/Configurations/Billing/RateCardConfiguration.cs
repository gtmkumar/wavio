using wavio.SharedDataModel.Entities.Billing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace wavio.SharedDataModel.Persistence.Configurations.Billing;

public sealed class RateCardConfiguration : IEntityTypeConfiguration<RateCard>
{
    public void Configure(EntityTypeBuilder<RateCard> builder)
    {
        builder.ToTable("rate_cards", "billing");

        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).HasColumnName("id").ValueGeneratedOnAdd();

        builder.Property(e => e.Name).HasColumnName("name").HasMaxLength(120).IsRequired();
        builder.Property(e => e.Currency).HasColumnName("currency").HasMaxLength(3).IsRequired();
        builder.Property(e => e.Source).HasColumnName("source").HasMaxLength(20).IsRequired();
        builder.Property(e => e.EffectiveFrom).HasColumnName("effective_from").IsRequired();
        builder.Property(e => e.EffectiveTo).HasColumnName("effective_to");
        builder.Property(e => e.Status).HasColumnName("status").HasMaxLength(20).IsRequired();
        builder.Property(e => e.Notes).HasColumnName("notes");

        builder.Property(e => e.CreatedAt).HasColumnName("created_at").IsRequired();
        builder.Property(e => e.UpdatedAt).HasColumnName("updated_at").IsRequired();
        builder.Property(e => e.CreatedBy).HasColumnName("created_by");
        builder.Property(e => e.UpdatedBy).HasColumnName("updated_by");
        builder.Property(e => e.Version).HasColumnName("version").IsRequired();

        builder.HasIndex(e => new { e.Currency, e.EffectiveFrom })
            .IsUnique()
            .HasDatabaseName("rate_cards_currency_effective_from_key");

        builder.HasMany(e => e.Entries)
            .WithOne()
            .HasForeignKey(e => e.RateCardId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
