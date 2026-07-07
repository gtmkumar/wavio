using WaAdmin.Application.Common.Interfaces;
using wavio.SharedDataModel.Entities.Consent;
using wavio.SharedDataModel.Entities.Messaging;
using wavio.SharedDataModel.Entities.Templates;
using wavio.SharedDataModel.Entities.Waba;
using Microsoft.EntityFrameworkCore;

namespace WaAdmin.Tests.Fakes;

/// <summary>
/// Minimal <see cref="IWaAdminDbContext"/> stand-in for unit tests. <c>DbSet&lt;T&gt;</c> cannot
/// be hand-faked, so this uses the EF Core in-memory provider — same pattern as WaIngest.Tests'
/// <c>InMemoryWaIngestDbContext</c>.
/// </summary>
public sealed class InMemoryWaAdminDbContext : DbContext, IWaAdminDbContext
{
    public InMemoryWaAdminDbContext(DbContextOptions<InMemoryWaAdminDbContext> options) : base(options) { }

    public DbSet<Template> Templates => Set<Template>();
    public DbSet<TemplateVersion> TemplateVersions => Set<TemplateVersion>();
    public DbSet<TemplateStatusEvent> TemplateStatusEvents => Set<TemplateStatusEvent>();
    public DbSet<TemplateCategoryChange> TemplateCategoryChanges => Set<TemplateCategoryChange>();
    public DbSet<TemplateLintResult> TemplateLintResults => Set<TemplateLintResult>();

    public DbSet<WabaPhoneNumber> WabaPhoneNumbers => Set<WabaPhoneNumber>();
    public DbSet<WabaBusinessAccount> WabaBusinessAccounts => Set<WabaBusinessAccount>();
    public DbSet<WabaBusinessProfile> WabaBusinessProfiles => Set<WabaBusinessProfile>();
    public DbSet<WabaPhoneNumberEvent> WabaPhoneNumberEvents => Set<WabaPhoneNumberEvent>();

    public DbSet<OptInEvent> OptInEvents => Set<OptInEvent>();
    public DbSet<OptOutEvent> OptOutEvents => Set<OptOutEvent>();
    public DbSet<ErasureRequest> ErasureRequests => Set<ErasureRequest>();
    public DbSet<RetentionPolicy> RetentionPolicies => Set<RetentionPolicy>();
    public DbSet<SuppressionListEntry> SuppressionListEntries => Set<SuppressionListEntry>();

    /// <summary>Test-configurable stand-in for the real raw-SQL lookup against
    /// waba.business_accounts (the in-memory provider doesn't support Database.SqlQuery) —
    /// populate directly in each test that needs a business account to resolve.</summary>
    public Dictionary<Guid, string> BusinessAccountMetaWabaIds { get; } = [];

    public Task<string?> GetBusinessAccountMetaWabaIdAsync(Guid businessAccountId, CancellationToken cancellationToken) =>
        Task.FromResult(BusinessAccountMetaWabaIds.TryGetValue(businessAccountId, out var id) ? id : null);

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Template>().HasKey(e => e.Id);
        modelBuilder.Entity<TemplateVersion>().HasKey(e => e.Id);
        modelBuilder.Entity<TemplateStatusEvent>().HasKey(e => e.Id);
        modelBuilder.Entity<TemplateCategoryChange>().HasKey(e => e.Id);
        modelBuilder.Entity<TemplateLintResult>().HasKey(e => e.Id);
        modelBuilder.Entity<OptInEvent>().HasKey(e => e.Id);
        modelBuilder.Entity<OptOutEvent>().HasKey(e => e.Id);
        modelBuilder.Entity<ErasureRequest>().HasKey(e => e.Id);
        modelBuilder.Entity<RetentionPolicy>().HasKey(e => e.Id);
        modelBuilder.Entity<SuppressionListEntry>().HasKey(e => e.Id);
        modelBuilder.Entity<WabaPhoneNumber>().HasKey(e => e.Id);
        modelBuilder.Entity<WabaBusinessAccount>().HasKey(e => e.Id);
        modelBuilder.Entity<WabaBusinessProfile>().HasKey(e => e.Id);
        modelBuilder.Entity<WabaPhoneNumberEvent>().HasKey(e => e.Id);
    }

    public static InMemoryWaAdminDbContext Create(string databaseName) =>
        new(new DbContextOptionsBuilder<InMemoryWaAdminDbContext>().UseInMemoryDatabase(databaseName).Options);
}
