using wavio.SharedDataModel.Entities.Billing;
using wavio.SharedDataModel.Entities.IdentityAccess;
using wavio.SharedDataModel.Entities.Ingest;
using wavio.SharedDataModel.Entities.Kernel;
using wavio.SharedDataModel.Entities.Messaging;
using wavio.SharedDataModel.Entities.Sessions;
using wavio.SharedDataModel.Entities.TenancyOrg;
using wavio.SharedDataModel.Entities.Templates;
using wavio.SharedDataModel.Entities.Waba;
using Microsoft.EntityFrameworkCore;

namespace wavio.SharedDataModel.Persistence;

/// <summary>
/// Shared EF Core DbContext mapping the live PostgreSQL database (database-first).
/// Do NOT run migrations against this context — the DB schema is canonical.
///
/// Soft-delete query filters (HasQueryFilter(e => e.DeletedAt == null)):
///   tenancy: Tenant
///   identity_access: User, Role
///   kernel: FileAttachment
///   templates: Template
/// All other entities do not have deleted_at and have no global filter.
/// Use IgnoreQueryFilters() when you need to see soft-deleted rows.
/// </summary>
public class WavioDbContext : DbContext
{
    public WavioDbContext(DbContextOptions<WavioDbContext> options)
        : base(options) { }

    // tenancy
    public DbSet<Tenant> Tenants => Set<Tenant>();

    // identity_access
    public DbSet<User> Users => Set<User>();
    public DbSet<UserProfile> UserProfiles => Set<UserProfile>();
    public DbSet<UserScopeMembership> UserScopeMemberships => Set<UserScopeMembership>();
    public DbSet<Role> Roles => Set<Role>();
    public DbSet<Permission> Permissions => Set<Permission>();
    public DbSet<RolePermission> RolePermissions => Set<RolePermission>();
    public DbSet<UserPermissionOverride> UserPermissionOverrides => Set<UserPermissionOverride>();
    public DbSet<OtpCode> OtpCodes => Set<OtpCode>();
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();
    public DbSet<LoginHistory> LoginHistories => Set<LoginHistory>();
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();
    public DbSet<PasswordReset> PasswordResets => Set<PasswordReset>();

    // kernel
    public DbSet<OutboxEvent> OutboxEvents => Set<OutboxEvent>();
    public DbSet<OutboxConsumedEvent> OutboxConsumedEvents => Set<OutboxConsumedEvent>();
    public DbSet<SystemSetting> SystemSettings => Set<SystemSetting>();
    public DbSet<FeatureFlag> FeatureFlags => Set<FeatureFlag>();
    public DbSet<FileAttachment> FileAttachments => Set<FileAttachment>();

    // ingest (issue #13: wa-ingest-svc webhook receiver)
    public DbSet<RawWebhook> RawWebhooks => Set<RawWebhook>();
    public DbSet<WebhookDedupe> WebhookDedupes => Set<WebhookDedupe>();

    // templates (issue #16: wa-admin-svc template lifecycle)
    public DbSet<Template> Templates => Set<Template>();
    public DbSet<TemplateVersion> TemplateVersions => Set<TemplateVersion>();
    public DbSet<TemplateStatusEvent> TemplateStatusEvents => Set<TemplateStatusEvent>();
    public DbSet<TemplateCategoryChange> TemplateCategoryChanges => Set<TemplateCategoryChange>();
    public DbSet<TemplateLintResult> TemplateLintResults => Set<TemplateLintResult>();

    // sessions (issue #15: Session Window Manager)
    public DbSet<ConversationWindow> ConversationWindows => Set<ConversationWindow>();
    public DbSet<WindowEvent> WindowEvents => Set<WindowEvent>();

    // messaging (issue #14: wa-gateway-svc outbound send API)
    public DbSet<OutboundMessage> OutboundMessages => Set<OutboundMessage>();
    public DbSet<OutboundOutboxEntry> OutboundOutboxEntries => Set<OutboundOutboxEntry>();

    // waba (issue #14: dispatcher's internal-id -> Meta phone_number_id bridge)
    public DbSet<WabaPhoneNumber> WabaPhoneNumbers => Set<WabaPhoneNumber>();

    // billing (issue #19: PMP cost ledger, rate cards, quotas/metering, invoice reconciliation)
    public DbSet<RateCard> RateCards => Set<RateCard>();
    public DbSet<RateCardEntry> RateCardEntries => Set<RateCardEntry>();
    public DbSet<MessageCost> MessageCosts => Set<MessageCost>();
    public DbSet<TenantQuota> TenantQuotas => Set<TenantQuota>();
    public DbSet<UsageCounter> UsageCounters => Set<UsageCounter>();
    public DbSet<InvoiceFeed> InvoicesFeed => Set<InvoiceFeed>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(WavioDbContext).Assembly);
    }
}
