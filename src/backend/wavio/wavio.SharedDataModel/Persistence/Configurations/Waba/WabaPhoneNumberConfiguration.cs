using wavio.SharedDataModel.Entities.Waba;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace wavio.SharedDataModel.Persistence.Configurations.Waba;

public sealed class WabaPhoneNumberConfiguration : IEntityTypeConfiguration<WabaPhoneNumber>
{
    public void Configure(EntityTypeBuilder<WabaPhoneNumber> builder)
    {
        builder.ToTable("phone_numbers", "waba");

        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).HasColumnName("id").ValueGeneratedOnAdd();

        builder.Property(e => e.TenantId).HasColumnName("tenant_id").IsRequired();
        builder.Property(e => e.BusinessAccountId).HasColumnName("business_account_id").IsRequired();
        builder.Property(e => e.MetaPhoneNumberId).HasColumnName("meta_phone_number_id").HasMaxLength(32).IsRequired();
        builder.Property(e => e.DisplayPhoneNumber).HasColumnName("display_phone_number").HasMaxLength(20).IsRequired();
        builder.Property(e => e.Status).HasColumnName("status").HasMaxLength(20).IsRequired();
        builder.Property(e => e.MessagingTier).HasColumnName("messaging_tier").HasMaxLength(20);

        builder.Property(e => e.CreatedAt).HasColumnName("created_at").IsRequired();
        builder.Property(e => e.UpdatedAt).HasColumnName("updated_at").IsRequired();
        builder.Property(e => e.Version).HasColumnName("version").IsRequired();

        builder.HasIndex(e => e.MetaPhoneNumberId)
            .IsUnique()
            .HasDatabaseName("phone_numbers_meta_phone_number_id_key");
    }
}
