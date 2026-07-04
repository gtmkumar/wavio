using WaIngest.Application.Ingestion.Dtos;
using Wavio.Utilities.CQRS.Abstractions;

namespace WaIngest.Application.Ingestion.Commands.PersistRawWebhook;

/// <summary>
/// Persists a webhook delivery verbatim to <c>ingest.raw_webhooks</c> BEFORE any parsing (spec
/// §4.3: durability first, ack second). <see cref="Payload"/> is the exact raw request body as
/// UTF-8 text (must be valid JSON for the jsonb column — the endpoint validates that before
/// issuing this command, since Meta always sends JSON).
/// </summary>
public sealed record PersistRawWebhookCommand(
    string Payload,
    string? Headers,
    bool SignatureValid) : ICommand<RawWebhookRef>;
