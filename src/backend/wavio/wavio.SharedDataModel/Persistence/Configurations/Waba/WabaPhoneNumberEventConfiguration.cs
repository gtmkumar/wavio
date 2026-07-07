using wavio.SharedDataModel.Entities.Waba;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace wavio.SharedDataModel.Persistence.Configurations.Waba;

public sealed class WabaPhoneNumberEventConfiguration : IEntityTypeConfiguration<WabaPhoneNumberEvent>
{
    public void Configure(EntityTypeBuilder<WabaPhoneNumberEvent> builder)
    {
        builder.ToTable("phone_number_events", "waba");

        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).HasColumnName("id").ValueGeneratedOnAdd();

        builder.Property(e => e.TenantId).HasColumnName("tenant_id").IsRequired();
        builder.Property(e => e.PhoneNumberId).HasColumnName("phone_number_id").IsRequired();
        builder.Property(e => e.EventType).HasColumnName("event_type").HasMaxLength(50).IsRequired();
        builder.Property(e => e.OldStatus).HasColumnName("old_status").HasMaxLength(20);
        builder.Property(e => e.NewStatus).HasColumnName("new_status").HasMaxLength(20);
        builder.Property(e => e.Payload).HasColumnName("payload").HasColumnType("jsonb");
        builder.Property(e => e.OccurredAt).HasColumnName("occurred_at").IsRequired();
        builder.Property(e => e.CreatedAt).HasColumnName("created_at").IsRequired();
        builder.Property(e => e.CreatedBy).HasColumnName("created_by");
    }
}
