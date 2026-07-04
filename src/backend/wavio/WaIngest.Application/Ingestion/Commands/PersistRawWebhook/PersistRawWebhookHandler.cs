using WaIngest.Application.Common.Interfaces;
using WaIngest.Application.Ingestion.Dtos;
using Wavio.Utilities.CQRS.Abstractions;
using wavio.SharedDataModel.Entities.Ingest;

namespace WaIngest.Application.Ingestion.Commands.PersistRawWebhook;

/// <summary>Inserts the raw webhook row and returns its identity for the caller to hand off to
/// the background processing queue. This is the ONLY step that runs before the HTTP 200 ack.</summary>
public sealed class PersistRawWebhookHandler
    : ICommandHandler<PersistRawWebhookCommand, RawWebhookRef>
{
    private readonly IWaIngestDbContext _db;

    public PersistRawWebhookHandler(IWaIngestDbContext db) => _db = db;

    public async Task<RawWebhookRef> HandleAsync(PersistRawWebhookCommand command, CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;

        var entity = new RawWebhook
        {
            Id = Guid.NewGuid(),
            ReceivedAt = now,
            Source = "meta",
            SignatureValid = command.SignatureValid,
            Headers = command.Headers,
            Payload = command.Payload,
            ProcessingStatus = "received",
            CreatedAt = now
        };

        _db.RawWebhooks.Add(entity);
        await _db.SaveChangesAsync(cancellationToken);

        return new RawWebhookRef(entity.Id, entity.ReceivedAt);
    }
}
