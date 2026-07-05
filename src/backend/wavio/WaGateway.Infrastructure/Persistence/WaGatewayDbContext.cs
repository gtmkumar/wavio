using WaGateway.Application.Common.Interfaces;
using wavio.SharedDataModel.Entities.Messaging;
using wavio.SharedDataModel.Entities.Quality;
using wavio.SharedDataModel.Entities.Templates;
using wavio.SharedDataModel.Entities.Waba;
using wavio.SharedDataModel.Persistence;
using Microsoft.EntityFrameworkCore;

namespace WaGateway.Infrastructure.Persistence;

/// <summary>
/// Adapts the shared <see cref="WavioDbContext"/> to <see cref="IWaGatewayDbContext"/>. Same
/// pattern as core.Infrastructure's <c>CoreDbContext</c> / WaIntel's <c>WaIntelDbContext</c>.
/// </summary>
public sealed class WaGatewayDbContext : IWaGatewayDbContext
{
    private readonly WavioDbContext _db;

    public WaGatewayDbContext(WavioDbContext db) => _db = db;

    public DbSet<OutboundMessage> OutboundMessages => _db.OutboundMessages;
    public DbSet<OutboundOutboxEntry> OutboundOutboxEntries => _db.OutboundOutboxEntries;
    public DbSet<WabaPhoneNumber> WabaPhoneNumbers => _db.WabaPhoneNumbers;
    public DbSet<GuardianIncident> GuardianIncidents => _db.GuardianIncidents;
    public DbSet<SuppressionListEntry> SuppressionListEntries => _db.SuppressionListEntries;
    public DbSet<Campaign> Campaigns => _db.Campaigns;
    public DbSet<CampaignRecipient> CampaignRecipients => _db.CampaignRecipients;
    public DbSet<Template> Templates => _db.Templates;
    public DbSet<TemplateVersion> TemplateVersions => _db.TemplateVersions;

    public Task<int> SaveChangesAsync(CancellationToken cancellationToken) =>
        _db.SaveChangesAsync(cancellationToken);
}
