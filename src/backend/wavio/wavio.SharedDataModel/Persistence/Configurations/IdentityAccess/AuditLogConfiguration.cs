using wavio.SharedDataModel.Entities.IdentityAccess;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace wavio.SharedDataModel.Persistence.Configurations.IdentityAccess;

public sealed class AuditLogConfiguration : IEntityTypeConfiguration<AuditLog>
{
    public void Configure(EntityTypeBuilder<AuditLog> b)
    {
        b.ToTable("audit_logs", "identity_access");

        // Composite PK required by PG range partitioning on occurred_at.
        b.HasKey(e => new { e.Id, e.OccurredAt });
        b.Property(e => e.Id).HasColumnName("id").ValueGeneratedOnAdd();
        b.Property(e => e.OccurredAt).HasColumnName("occurred_at").IsRequired();

        b.Property(e => e.TenantId).HasColumnName("tenant_id");
        b.Property(e => e.ActorUserId).HasColumnName("actor_user_id");
        b.Property(e => e.ActorType).HasColumnName("actor_type").HasMaxLength(20).IsRequired();
        b.Property(e => e.ActorDisplay).HasColumnName("actor_display").HasMaxLength(200);
        b.Property(e => e.Action).HasColumnName("action").HasMaxLength(100).IsRequired();
        b.Property(e => e.ResourceType).HasColumnName("resource_type").HasMaxLength(50).IsRequired();
        b.Property(e => e.ResourceId).HasColumnName("resource_id");
        b.Property(e => e.ResourceDisplay).HasColumnName("resource_display").HasMaxLength(200);
        b.Property(e => e.OldValues).HasColumnName("old_values").HasColumnType("jsonb");
        b.Property(e => e.NewValues).HasColumnName("new_values").HasColumnType("jsonb");
        b.Property(e => e.ChangedFields).HasColumnName("changed_fields").HasColumnType("text[]");
        b.Property(e => e.IpAddress).HasColumnName("ip_address").HasColumnType("inet");
        b.Property(e => e.UserAgent).HasColumnName("user_agent");
        b.Property(e => e.RequestId).HasColumnName("request_id");
        b.Property(e => e.CorrelationId).HasColumnName("correlation_id");
        b.Property(e => e.Success).HasColumnName("success").IsRequired();
        b.Property(e => e.ErrorMessage).HasColumnName("error_message");
        b.Property(e => e.CreatedAt).HasColumnName("created_at").IsRequired();
        b.Property(e => e.CreatedBy).HasColumnName("created_by");

        b.HasOne(e => e.Tenant)
            .WithMany()
            .HasForeignKey(e => e.TenantId)
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("audit_logs_tenant_id_fkey");
    }
}
