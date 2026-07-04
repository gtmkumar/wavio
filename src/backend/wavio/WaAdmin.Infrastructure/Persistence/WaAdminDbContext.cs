using WaAdmin.Application.Common.Interfaces;
using wavio.SharedDataModel.Entities.Templates;
using wavio.SharedDataModel.Persistence;
using Microsoft.EntityFrameworkCore;

namespace WaAdmin.Infrastructure.Persistence;

/// <summary>
/// Adapts the shared <see cref="WavioDbContext"/> to <see cref="IWaAdminDbContext"/>, exposing
/// only the <c>templates</c> schema entity sets this service owns (issue #16). Same pattern as
/// WaIngest's <c>WaIngestDbContext</c> / core's <c>CoreDbContext</c>.
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

    public async Task<string?> GetBusinessAccountMetaWabaIdAsync(Guid businessAccountId, CancellationToken cancellationToken)
    {
        var rows = await _db.Database
            .SqlQuery<string?>(
                $"SELECT meta_waba_id FROM waba.business_accounts WHERE id = {businessAccountId}")
            .ToListAsync(cancellationToken);
        return rows.Count > 0 ? rows[0] : null;
    }

    public Task<int> SaveChangesAsync(CancellationToken cancellationToken) =>
        _db.SaveChangesAsync(cancellationToken);
}
