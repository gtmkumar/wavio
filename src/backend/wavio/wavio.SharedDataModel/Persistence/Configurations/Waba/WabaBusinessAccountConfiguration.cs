using wavio.SharedDataModel.Entities.Waba;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace wavio.SharedDataModel.Persistence.Configurations.Waba;

public sealed class WabaBusinessAccountConfiguration : IEntityTypeConfiguration<WabaBusinessAccount>
{
    public void Configure(EntityTypeBuilder<WabaBusinessAccount> builder)
    {
        builder.ToTable("business_accounts", "waba");

        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).HasColumnName("id").ValueGeneratedOnAdd();

        builder.Property(e => e.TenantId).HasColumnName("tenant_id").IsRequired();
        builder.Property(e => e.MetaWabaId).HasColumnName("meta_waba_id").HasMaxLength(32).IsRequired();
        builder.Property(e => e.Name).HasColumnName("name").HasMaxLength(200).IsRequired();
        builder.Property(e => e.CurrencyCode).HasColumnName("currency_code").HasMaxLength(3).IsFixedLength();
        builder.Property(e => e.MessageTemplateNamespace).HasColumnName("message_template_namespace").HasMaxLength(100);
        builder.Property(e => e.SystemUserTokenCiphertext).HasColumnName("system_user_token_ciphertext");
        builder.Property(e => e.TokenKeyRef).HasColumnName("token_key_ref").HasMaxLength(100);
        builder.Property(e => e.Status).HasColumnName("status").HasMaxLength(20).IsRequired();
        builder.Property(e => e.VerificationStatus).HasColumnName("verification_status").HasMaxLength(30);
        builder.Property(e => e.WebhooksSubscribedAt).HasColumnName("webhooks_subscribed_at");

        builder.Property(e => e.CreatedAt).HasColumnName("created_at").IsRequired();
        builder.Property(e => e.UpdatedAt).HasColumnName("updated_at").IsRequired();
        builder.Property(e => e.CreatedBy).HasColumnName("created_by");
        builder.Property(e => e.UpdatedBy).HasColumnName("updated_by");
        builder.Property(e => e.Version).HasColumnName("version").IsRequired();

        builder.HasIndex(e => e.MetaWabaId)
            .IsUnique()
            .HasDatabaseName("business_accounts_meta_waba_id_key");
    }
}
