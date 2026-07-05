using wavio.SharedDataModel.Entities.Quality;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace wavio.SharedDataModel.Persistence.Configurations.Quality;

public sealed class GuardianIncidentConfiguration : IEntityTypeConfiguration<GuardianIncident>
{
    public void Configure(EntityTypeBuilder<GuardianIncident> builder)
    {
        builder.ToTable("guardian_incidents", "quality");

        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).HasColumnName("id").ValueGeneratedOnAdd();

        builder.Property(e => e.TenantId).HasColumnName("tenant_id").IsRequired();
        builder.Property(e => e.PhoneNumberId).HasColumnName("phone_number_id").IsRequired();
        builder.Property(e => e.IncidentType).HasColumnName("incident_type").HasMaxLength(30).IsRequired();
        builder.Property(e => e.Severity).HasColumnName("severity").HasMaxLength(10).IsRequired();
        builder.Property(e => e.Status).HasColumnName("status").HasMaxLength(20).IsRequired();
        builder.Property(e => e.ThrottleAction).HasColumnName("throttle_action").HasMaxLength(30).IsRequired();
        builder.Property(e => e.TriggerRating).HasColumnName("trigger_rating").HasMaxLength(10);
        builder.Property(e => e.Details).HasColumnName("details").HasColumnType("jsonb");
        builder.Property(e => e.OpenedAt).HasColumnName("opened_at").IsRequired();
        builder.Property(e => e.ResolvedAt).HasColumnName("resolved_at");

        builder.Property(e => e.CreatedAt).HasColumnName("created_at").IsRequired();
        builder.Property(e => e.UpdatedAt).HasColumnName("updated_at").IsRequired();
        builder.Property(e => e.CreatedBy).HasColumnName("created_by");
        builder.Property(e => e.UpdatedBy).HasColumnName("updated_by");
        builder.Property(e => e.Version).HasColumnName("version").IsRequired();

        builder.HasIndex(e => new { e.PhoneNumberId, e.OpenedAt })
            .HasDatabaseName("guardian_incidents_number_opened_at_idx");
        // Mirrors the DB's partial index (db/migrations/V011) — EF only needs the shape for
        // query planning awareness; the actual partial WHERE lives in the migration.
        builder.HasIndex(e => e.TenantId)
            .HasDatabaseName("guardian_incidents_tenant_open_idx");
    }
}
