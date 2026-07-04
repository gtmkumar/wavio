using WaPlatform.Contracts.IntegrationEvents;

namespace WaIngest.Application.Ingestion.Normalization;

/// <summary>
/// One normalized sub-event extracted from a raw Meta webhook payload, paired with the dedupe
/// key <see cref="WebhookProcessor"/> uses to guarantee "duplicate webhook → one bus event"
/// (spec §4.3). A single raw delivery can (and often does) contain several of these — e.g. a
/// batched status webhook with three <c>statuses[]</c> entries yields three
/// <see cref="NormalizedWebhookEvent"/> instances.
/// </summary>
/// <param name="DedupeKey">Maps to <c>ingest.webhook_dedupe.wamid</c>. A real WhatsApp message id
/// for message/status/flow-response events; a stable SHA-256 hash of the relevant Meta payload
/// fragment for event kinds that have no natural message id (template/quality/tier/account).</param>
/// <param name="DedupeEventType">Maps to <c>ingest.webhook_dedupe.event_type</c>. Combined with
/// <paramref name="DedupeKey"/> this must uniquely identify "this exact change", not just "this
/// wamid" — e.g. message-status uses <c>{routingKey}:{status}</c> so sent/delivered/read against
/// the same wamid don't collide.</param>
/// <param name="Event">The typed contract event to publish (WaPlatform.Contracts).</param>
public sealed record NormalizedWebhookEvent(
    string DedupeKey,
    string DedupeEventType,
    IntegrationEvent Event);
