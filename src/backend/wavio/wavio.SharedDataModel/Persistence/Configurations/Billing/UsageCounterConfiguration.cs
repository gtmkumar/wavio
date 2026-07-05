using wavio.SharedDataModel.Entities.Billing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace wavio.SharedDataModel.Persistence.Configurations.Billing;

public sealed class UsageCounterConfiguration : IEntityTypeConfiguration<UsageCounter>
{
    public void Configure(EntityTypeBuilder<UsageCounter> builder)
    {
        builder.ToTable("usage_counters", "billing");

        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).HasColumnName("id").ValueGeneratedOnAdd();

        builder.Property(e => e.TenantId).HasColumnName("tenant_id").IsRequired();
        builder.Property(e => e.Category).HasColumnName("category").HasMaxLength(30).IsRequired();
        builder.Property(e => e.Period).HasColumnName("period").HasMaxLength(10).IsRequired();
        builder.Property(e => e.PeriodStart).HasColumnName("period_start").IsRequired();
        builder.Property(e => e.MessageCount).HasColumnName("message_count").IsRequired();
        builder.Property(e => e.BillableAmount).HasColumnName("billable_amount").HasColumnType("numeric(14,6)").IsRequired();
        builder.Property(e => e.Currency).HasColumnName("currency").HasMaxLength(3).IsRequired();
        builder.Property(e => e.SoftLimitAlertedAt).HasColumnName("soft_limit_alerted_at");
        builder.Property(e => e.HardLimitBlockedAt).HasColumnName("hard_limit_blocked_at");

        builder.Property(e => e.CreatedAt).HasColumnName("created_at").IsRequired();
        builder.Property(e => e.UpdatedAt).HasColumnName("updated_at").IsRequired();
        builder.Property(e => e.CreatedBy).HasColumnName("created_by");
        builder.Property(e => e.UpdatedBy).HasColumnName("updated_by");
        builder.Property(e => e.Version).HasColumnName("version").IsRequired();

        builder.HasIndex(e => new { e.TenantId, e.Category, e.Period, e.PeriodStart })
            .IsUnique()
            .HasDatabaseName("usage_counters_tenant_id_category_period_start_key");
    }
}
