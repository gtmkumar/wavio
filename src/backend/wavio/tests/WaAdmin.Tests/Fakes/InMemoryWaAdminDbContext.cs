using WaAdmin.Application.Common.Interfaces;
using wavio.SharedDataModel.Entities.Templates;
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
    }

    public static InMemoryWaAdminDbContext Create(string databaseName) =>
        new(new DbContextOptionsBuilder<InMemoryWaAdminDbContext>().UseInMemoryDatabase(databaseName).Options);
}
