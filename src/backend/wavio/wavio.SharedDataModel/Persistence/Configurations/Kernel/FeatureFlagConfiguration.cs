using wavio.SharedDataModel.Entities.Kernel;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace wavio.SharedDataModel.Persistence.Configurations.Kernel;

public sealed class FeatureFlagConfiguration : IEntityTypeConfiguration<FeatureFlag>
{
    public void Configure(EntityTypeBuilder<FeatureFlag> b)
    {
        b.ToTable("feature_flags", "system");

        b.HasKey(e => e.Id);
        b.Property(e => e.Id).HasColumnName("id").ValueGeneratedOnAdd();

        b.Property(e => e.TenantId).HasColumnName("tenant_id");
        b.Property(e => e.FlagKey).HasColumnName("flag_key").HasMaxLength(100).IsRequired();
        b.Property(e => e.Name).HasColumnName("name").HasMaxLength(200).IsRequired();
        b.Property(e => e.Description).HasColumnName("description");
        b.Property(e => e.FlagType).HasColumnName("flag_type").HasMaxLength(20).IsRequired();
        b.Property(e => e.DefaultValue).HasColumnName("default_value").IsRequired();
        b.Property(e => e.IsEnabled).HasColumnName("is_enabled").IsRequired();
        b.Property(e => e.RolloutPercent).HasColumnName("rollout_percent");
        b.Property(e => e.TargetSegments).HasColumnName("target_segments").HasColumnType("text[]");
        b.Property(e => e.TargetUserIds).HasColumnName("target_user_ids").HasColumnType("uuid[]");
        b.Property(e => e.TargetCities).HasColumnName("target_cities").HasColumnType("text[]");
        b.Property(e => e.Variants).HasColumnName("variants").HasColumnType("jsonb");
        b.Property(e => e.StartsAt).HasColumnName("starts_at");
        b.Property(e => e.EndsAt).HasColumnName("ends_at");
        b.Property(e => e.LastEvaluatedAt).HasColumnName("last_evaluated_at");
        b.Property(e => e.EvaluationCount).HasColumnName("evaluation_count").IsRequired();
        b.Property(e => e.Metadata).HasColumnName("metadata").HasColumnType("jsonb").IsRequired();
        b.Property(e => e.CreatedAt).HasColumnName("created_at").IsRequired();
        b.Property(e => e.UpdatedAt).HasColumnName("updated_at").IsRequired();
        b.Property(e => e.CreatedBy).HasColumnName("created_by");
        b.Property(e => e.UpdatedBy).HasColumnName("updated_by");
        b.Property(e => e.Status).HasColumnName("status").HasMaxLength(20).IsRequired();

        b.HasIndex(e => new { e.TenantId, e.FlagKey }).IsUnique().HasDatabaseName("feature_flags_tenant_id_flag_key_key");
    }
}
