using wavio.SharedDataModel.Entities.Sessions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace wavio.SharedDataModel.Persistence.Configurations.Sessions;

public sealed class WindowEventConfiguration : IEntityTypeConfiguration<WindowEvent>
{
    public void Configure(EntityTypeBuilder<WindowEvent> builder)
    {
        builder.ToTable("window_events", "sessions");

        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).HasColumnName("id").ValueGeneratedOnAdd();

        builder.Property(e => e.TenantId).HasColumnName("tenant_id").IsRequired();
        builder.Property(e => e.ConversationWindowId).HasColumnName("conversation_window_id").IsRequired();
        builder.Property(e => e.EventType).HasColumnName("event_type").HasMaxLength(30).IsRequired();
        builder.Property(e => e.OldExpiresAt).HasColumnName("old_expires_at");
        builder.Property(e => e.NewExpiresAt).HasColumnName("new_expires_at");
        builder.Property(e => e.Payload).HasColumnName("payload").HasColumnType("jsonb");
        builder.Property(e => e.OccurredAt).HasColumnName("occurred_at").IsRequired();
        builder.Property(e => e.CreatedAt).HasColumnName("created_at").IsRequired();
        builder.Property(e => e.CreatedBy).HasColumnName("created_by");

        builder.HasIndex(e => new { e.ConversationWindowId, e.OccurredAt })
            .HasDatabaseName("window_events_conversation_window_id_occurred_at_idx");
        builder.HasIndex(e => e.TenantId)
            .HasDatabaseName("window_events_tenant_id_idx");
    }
}
