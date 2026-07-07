using WaAdmin.Application.Common.Interfaces;
using wavio.SharedDataModel.Entities.Consent;
using wavio.SharedDataModel.Entities.Messaging;
using wavio.SharedDataModel.Entities.Templates;
using wavio.SharedDataModel.Entities.Waba;
using wavio.SharedDataModel.Persistence;
using Microsoft.EntityFrameworkCore;

namespace WaAdmin.Infrastructure.Persistence;

/// <summary>
/// Adapts the shared <see cref="WavioDbContext"/> to <see cref="IWaAdminDbContext"/>, exposing
/// the <c>templates</c> schema entity sets this service owns (issue #16) plus the <c>consent</c>
/// schema and <c>messaging.suppression_list</c> (issue #21). Same pattern as WaIngest's
/// <c>WaIngestDbContext</c> / core's <c>CoreDbContext</c>.
/// </summary>
public sealed class WaAdminDbContext : IWaAdminDbContext
{
    private readonly WavioDbContext _db;

    public WaAdminDbContext(WavioDbContext db) => _db = db;

    public DbSet<Template> Templates => _db.Templates;
    public DbSet<TemplateVersion> TemplateVersions => _db.TemplateVersions;
    public DbSet<TemplateStatusEvent> TemplateStatusEvents => _db.TemplateStatusEvents;
    public DbSet<TemplateCategoryChange> TemplateCategoryChanges => _db.TemplateCategoryChanges;
    public DbSet<TemplateLintResult> TemplateLintResults => _db.TemplateLintResults;

    public DbSet<WabaPhoneNumber> WabaPhoneNumbers => _db.WabaPhoneNumbers;
    public DbSet<WabaBusinessAccount> WabaBusinessAccounts => _db.WabaBusinessAccounts;
    public DbSet<WabaBusinessProfile> WabaBusinessProfiles => _db.WabaBusinessProfiles;
    public DbSet<WabaPhoneNumberEvent> WabaPhoneNumberEvents => _db.WabaPhoneNumberEvents;

    public DbSet<OptInEvent> OptInEvents => _db.OptInEvents;
    public DbSet<OptOutEvent> OptOutEvents => _db.OptOutEvents;
    public DbSet<ErasureRequest> ErasureRequests => _db.ErasureRequests;
    public DbSet<RetentionPolicy> RetentionPolicies => _db.RetentionPolicies;
    public DbSet<SuppressionListEntry> SuppressionListEntries => _db.SuppressionListEntries;

    public Task<string?> GetBusinessAccountMetaWabaIdAsync(Guid businessAccountId, CancellationToken cancellationToken) =>
        // Was a raw scalar SQL query while no waba.business_accounts entity existed; the
        // onboarding wizard added WabaBusinessAccount, so this now goes through EF like
        // everything else.
        _db.WabaBusinessAccounts.AsNoTracking()
            .Where(b => b.Id == businessAccountId)
            .Select(b => (string?)b.MetaWabaId)
            .FirstOrDefaultAsync(cancellationToken);

    public Task<int> SaveChangesAsync(CancellationToken cancellationToken) =>
        _db.SaveChangesAsync(cancellationToken);
}
