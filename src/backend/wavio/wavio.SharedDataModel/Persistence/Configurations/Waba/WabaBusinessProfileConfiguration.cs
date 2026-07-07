using wavio.SharedDataModel.Entities.Waba;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace wavio.SharedDataModel.Persistence.Configurations.Waba;

public sealed class WabaBusinessProfileConfiguration : IEntityTypeConfiguration<WabaBusinessProfile>
{
    public void Configure(EntityTypeBuilder<WabaBusinessProfile> builder)
    {
        builder.ToTable("business_profiles", "waba");

        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).HasColumnName("id").ValueGeneratedOnAdd();

        builder.Property(e => e.TenantId).HasColumnName("tenant_id").IsRequired();
        builder.Property(e => e.PhoneNumberId).HasColumnName("phone_number_id").IsRequired();
        builder.Property(e => e.About).HasColumnName("about").HasMaxLength(139);
        builder.Property(e => e.Address).HasColumnName("address").HasMaxLength(256);
        builder.Property(e => e.Description).HasColumnName("description").HasMaxLength(512);
        builder.Property(e => e.Email).HasColumnName("email").HasMaxLength(128);
        builder.Property(e => e.Websites).HasColumnName("websites");
        builder.Property(e => e.Vertical).HasColumnName("vertical").HasMaxLength(50);
        builder.Property(e => e.ProfilePictureUrl).HasColumnName("profile_picture_url");

        builder.Property(e => e.CreatedAt).HasColumnName("created_at").IsRequired();
        builder.Property(e => e.UpdatedAt).HasColumnName("updated_at").IsRequired();
        builder.Property(e => e.CreatedBy).HasColumnName("created_by");
        builder.Property(e => e.UpdatedBy).HasColumnName("updated_by");
        builder.Property(e => e.Version).HasColumnName("version").IsRequired();

        builder.HasIndex(e => e.PhoneNumberId)
            .IsUnique()
            .HasDatabaseName("business_profiles_phone_number_id_key");
    }
}
