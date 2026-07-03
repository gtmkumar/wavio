using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using WaPlatform.Contracts.IntegrationEvents.V1;

namespace WaIngest.Application.Ingestion.Normalization;

/// <summary>
/// Normalizes a raw Meta Cloud API webhook payload (spec §4.3, §7.2) into the platform's typed
/// integration events. Pure function of its input — no I/O, no DB, no bus — so it is fully
/// unit-testable with recorded/synthetic payloads (see WaIngest.Tests).
///
/// Tenant resolution (spec §5): Wave 1 has no onboarding flow (issue #6) yet, so
/// <c>waba.phone_numbers</c> is empty and — even once populated — is RLS-scoped, which an
/// unauthenticated webhook receiver cannot query without already knowing the tenant. Every event
/// below is therefore published with <c>TenantId = Guid.Empty</c> (unresolved) and carries Meta's
/// raw <c>phone_number_id</c>/<c>waba_id</c> strings instead (already part of every contract).
/// A real resolution path (a narrow, audited platform_admin-scoped lookup, per spec §5) is future
/// work once WaAdmin's onboarding (#6) provisions real waba rows.
///
/// Known Wave-1 fidelity gaps (documented, not silently assumed):
///   • Template/quality/tier webhooks carry no "previous state" from Meta for every field — where
///     Meta doesn't supply it (template status, quality rating) we emit "UNKNOWN"/null rather than
///     guessing; Wave 2 (#20 Guardian, #16 templates) adds real state tracking.
///   • Platform-side <c>TemplateId</c>/<c>FlowId</c> Guids cannot be resolved here (no
///     templates/flows tables yet) — <see cref="Guid.Empty"/>, with Meta's own string id carried
///     alongside for correlation.
///   • Payment-status detection (order_details) is a best-effort shape guess: Wave 3 (#26) is
///     when real UPI payment payloads will be available to harden it against.
/// </summary>
public static class MetaWebhookNormalizer
{
    // Meta webhook "changes[].field" values this normalizer understands.
    private const string FieldMessages = "messages";
    private const string FieldTemplateStatus = "message_template_status_update";
    private const string FieldTemplateCategory = "message_template_category_update";
    private const string FieldPhoneQuality = "phone_number_quality_update";
    private const string FieldAccountUpdate = "account_update";

    /// <summary>
    /// Normalizes one full webhook delivery. Never throws on malformed/unexpected shapes —
    /// unrecognized fields/fragments are skipped (returned via <paramref name="skipped"/> reasons)
    /// so one bad sub-event in a batch never blocks the rest.
    /// </summary>
    public static IReadOnlyList<NormalizedWebhookEvent> Normalize(
        JsonElement root, out IReadOnlyList<string> skipped)
    {
        var results = new List<NormalizedWebhookEvent>();
        var skips = new List<string>();

        if (!TryGetArray(root, "entry", out var entries))
        {
            skips.Add("root has no entry[] array");
            skipped = skips;
            return results;
        }

        foreach (var entry in entries)
        {
            var wabaId = GetString(entry, "id") ?? string.Empty;

            if (!TryGetArray(entry, "changes", out var changes))
                continue;

            foreach (var change in changes)
            {
                var field = GetString(change, "field") ?? string.Empty;
                if (!change.TryGetProperty("value", out var value))
                {
                    skips.Add($"change with field '{field}' has no value object");
                    continue;
                }

                try
                {
                    switch (field)
                    {
                        case FieldMessages:
                            NormalizeMessagesField(value, wabaId, results, skips);
                            break;
                        case FieldTemplateStatus:
                            NormalizeTemplateStatus(value, results);
                            break;
                        case FieldTemplateCategory:
                            NormalizeTemplateCategory(value, results);
                            break;
                        case FieldPhoneQuality:
                            NormalizePhoneQuality(value, wabaId, results);
                            break;
                        case FieldAccountUpdate:
                            NormalizeAccountUpdate(value, wabaId, results);
                            break;
                        default:
                            skips.Add($"unrecognized field '{field}'");
                            break;
                    }
                }
                catch (Exception ex) when (ex is KeyNotFoundException or InvalidOperationException or FormatException)
                {
                    // Defensive: a malformed fragment must never take down the whole batch.
                    skips.Add($"field '{field}' fragment could not be parsed: {ex.GetType().Name}");
                }
            }
        }

        skipped = skips;
        return results;
    }

    // ── messages / statuses ─────────────────────────────────────────────────────────────

    private static void NormalizeMessagesField(
        JsonElement value, string wabaId, List<NormalizedWebhookEvent> results, List<string> skips)
    {
        var phoneNumberId = GetString(value, "metadata", "phone_number_id") ?? string.Empty;

        if (TryGetArray(value, "messages", out var messages))
        {
            foreach (var message in messages)
                NormalizeInboundMessage(message, phoneNumberId, wabaId, results, skips);
        }

        if (TryGetArray(value, "statuses", out var statuses))
        {
            foreach (var status in statuses)
                NormalizeStatus(status, phoneNumberId, results, skips);
        }
    }

    private static void NormalizeInboundMessage(
        JsonElement message, string phoneNumberId, string wabaId,
        List<NormalizedWebhookEvent> results, List<string> skips)
    {
        var wamid = GetString(message, "id");
        var waId = GetString(message, "from");
        var type = GetString(message, "type") ?? "unknown";
        var sentAt = ParseUnixSeconds(GetString(message, "timestamp"));

        if (wamid is null || waId is null)
        {
            skips.Add("inbound message missing id or from");
            return;
        }

        // WhatsApp Flow completion arrives as an interactive "nfm_reply" message.
        if (type == "interactive"
            && GetString(message, "interactive", "type") == "nfm_reply")
        {
            var flowName = GetString(message, "interactive", "nfm_reply", "name") ?? "unknown";
            var responseJson = GetString(message, "interactive", "nfm_reply", "response_json") ?? "{}";

            var flowEvent = new FlowResponseV1
            {
                FlowId = flowName,
                Wamid = wamid,
                WaId = waId,
                ResponseJson = responseJson
            };

            results.Add(new NormalizedWebhookEvent(wamid, FlowResponseV1.Name, flowEvent));
            return;
        }

        var evt = new MessageReceivedV1
        {
            Wamid = wamid,
            WaId = waId,
            PhoneNumberId = phoneNumberId,
            WabaId = wabaId,
            MessageType = type,
            SentAt = sentAt ?? DateTimeOffset.UtcNow
        };

        results.Add(new NormalizedWebhookEvent(wamid, MessageReceivedV1.Name, evt));
    }

    private static void NormalizeStatus(
        JsonElement status, string phoneNumberId, List<NormalizedWebhookEvent> results, List<string> skips)
    {
        var wamid = GetString(status, "id");
        var statusValue = GetString(status, "status");

        if (wamid is null || statusValue is null)
        {
            skips.Add("status update missing id or status");
            return;
        }

        // Best-effort: WhatsApp Payments (order_details) status detection (spec §4.9). No
        // production payload has been observed yet (Wave 3, issue #26) — hardened when that
        // module lands. Falls through to the normal MessageStatusV1 path when absent.
        if (status.TryGetProperty("payment", out var payment))
        {
            var referenceId = GetString(payment, "reference_id") ?? wamid;
            var amountMinor = payment.TryGetProperty("amount", out var amount)
                && amount.TryGetProperty("value", out var amountValue)
                && amountValue.TryGetInt64(out var minorUnits)
                    ? minorUnits
                    : 0L;
            var currency = GetString(payment, "amount", "currency") ?? "INR";
            var pspTxnId = GetString(payment, "transaction_id");

            var paymentEvent = new PaymentStatusV1
            {
                ReferenceId = referenceId,
                Status = MapPaymentStatus(statusValue),
                AmountMinorUnits = amountMinor,
                Currency = currency,
                PspTransactionId = pspTxnId
            };

            results.Add(new NormalizedWebhookEvent(
                wamid, $"{PaymentStatusV1.Name}:{BoundDedupeSuffix(statusValue)}", paymentEvent));
            return;
        }

        int? errorCode = null;
        if (TryGetArray(status, "errors", out var errors))
        {
            foreach (var error in errors)
            {
                if (error.TryGetProperty("code", out var codeEl) && codeEl.TryGetInt32(out var code))
                {
                    errorCode = code;
                    break;
                }
            }
        }

        bool? billable = null;
        string? pricingCategory = null;
        string? pricingModel = null;
        if (status.TryGetProperty("pricing", out var pricing))
        {
            if (pricing.TryGetProperty("billable", out var billableEl)
                && billableEl.ValueKind is JsonValueKind.True or JsonValueKind.False)
                billable = billableEl.GetBoolean();

            pricingCategory = GetString(pricing, "category");
            pricingModel = GetString(pricing, "pricing_model");
        }

        var statusEvent = new MessageStatusV1
        {
            Wamid = wamid,
            PhoneNumberId = phoneNumberId,
            Status = statusValue,
            ErrorCode = errorCode,
            Billable = billable,
            PricingCategory = pricingCategory,
            PricingModel = pricingModel
        };

        // Dedupe per (wamid, status) — the same wamid legitimately progresses through
        // sent -> delivered -> read, each of which must publish, not collide.
        results.Add(new NormalizedWebhookEvent(wamid, $"{MessageStatusV1.Name}:{BoundDedupeSuffix(statusValue)}", statusEvent));
    }

    // ingest.webhook_dedupe.event_type is varchar(50); {routing-key}:{suffix} must fit. Meta's own
    // status vocabulary (sent/delivered/read/failed, or payment statuses like captured/expired) is
    // always well under this, but an unexpected/malformed value must never make the dedupe INSERT
    // itself fail — that would happen AFTER a successful publish (see WebhookProcessor's ordering),
    // which would incorrectly leave the row 'failed' and cause a replay to publish a duplicate.
    private const int MaxDedupeSuffixLength = 20;

    private static string BoundDedupeSuffix(string value) =>
        value.Length <= MaxDedupeSuffixLength ? value : value[..MaxDedupeSuffixLength];

    private static string MapPaymentStatus(string metaStatus) => metaStatus.ToLowerInvariant() switch
    {
        "captured" or "success" or "sent" => "success",
        "failed" => "failed",
        "expired" => "expired",
        "refunded" => "refunded",
        _ => "pending"
    };

    // ── template lifecycle ──────────────────────────────────────────────────────────────

    private static void NormalizeTemplateStatus(JsonElement value, List<NormalizedWebhookEvent> results)
    {
        var metaTemplateId = GetString(value, "message_template_id") ?? string.Empty;
        var newStatus = GetString(value, "event") ?? "unknown";
        var reason = GetString(value, "reason");

        var evt = new TemplateStatusChangedV1
        {
            // Platform-side template id cannot be resolved here (no templates schema wiring in
            // wa-ingest-svc, issue #16 owns that table) — Guid.Empty, documented above.
            TemplateId = Guid.Empty,
            MetaTemplateId = metaTemplateId,
            // Meta's message_template_status_update webhook does not report the prior status.
            PreviousStatus = "UNKNOWN",
            NewStatus = newStatus,
            Reason = reason
        };

        var key = HashKey(FieldTemplateStatus, value);
        results.Add(new NormalizedWebhookEvent(key, TemplateStatusChangedV1.Name, evt));
    }

    private static void NormalizeTemplateCategory(JsonElement value, List<NormalizedWebhookEvent> results)
    {
        var metaTemplateId = GetString(value, "message_template_id") ?? string.Empty;
        var previousCategory = GetString(value, "previous_category") ?? "UNKNOWN";
        var newCategory = GetString(value, "new_category") ?? "UNKNOWN";

        var evt = new TemplateCategoryChangedV1
        {
            TemplateId = Guid.Empty,
            MetaTemplateId = metaTemplateId,
            PreviousCategory = previousCategory,
            NewCategory = newCategory
        };

        var key = HashKey(FieldTemplateCategory, value);
        results.Add(new NormalizedWebhookEvent(key, TemplateCategoryChangedV1.Name, evt));
    }

    // ── quality / tier ──────────────────────────────────────────────────────────────────

    private static void NormalizePhoneQuality(JsonElement value, string wabaId, List<NormalizedWebhookEvent> results)
    {
        var phoneNumberId = GetString(value, "phone_number_id") ?? GetString(value, "display_phone_number") ?? string.Empty;
        var currentRating = GetString(value, "event") ?? "UNKNOWN";
        var currentTier = GetString(value, "current_limit");

        var key = HashKey(FieldPhoneQuality, value);

        var qualityEvent = new QualityChangedV1
        {
            PhoneNumberId = phoneNumberId,
            WabaId = wabaId,
            // No persisted prior-quality snapshot in Wave 1 (Wave 2 #20 Guardian owns that state).
            PreviousRating = "UNKNOWN",
            CurrentRating = currentRating,
            MessagingTier = currentTier ?? "UNKNOWN",
            AutoThrottleApplied = false
        };
        results.Add(new NormalizedWebhookEvent(key, QualityChangedV1.Name, qualityEvent));

        if (currentTier is not null)
        {
            var tierEvent = new TierChangedV1
            {
                PhoneNumberId = phoneNumberId,
                WabaId = wabaId,
                PreviousTier = null,
                NewTier = currentTier
            };
            results.Add(new NormalizedWebhookEvent(key, TierChangedV1.Name, tierEvent));
        }
    }

    // ── account alerts ──────────────────────────────────────────────────────────────────

    private static void NormalizeAccountUpdate(JsonElement value, string wabaId, List<NormalizedWebhookEvent> results)
    {
        var alertType = GetString(value, "event") ?? "account_update";
        var severity = alertType.Contains("BAN", StringComparison.OrdinalIgnoreCase)
            || alertType.Contains("DISABLE", StringComparison.OrdinalIgnoreCase)
            || alertType.Contains("VIOLATION", StringComparison.OrdinalIgnoreCase)
                ? "critical"
                : "warning";

        var detail = value.GetRawText();
        if (detail.Length > 1000) detail = detail[..1000];

        var evt = new AccountAlertV1
        {
            WabaId = wabaId,
            AlertType = alertType,
            Severity = severity,
            Detail = detail
        };

        var key = HashKey(FieldAccountUpdate, value);
        results.Add(new NormalizedWebhookEvent(key, AccountAlertV1.Name, evt));
    }

    // ── shared helpers ──────────────────────────────────────────────────────────────────

    /// <summary>
    /// Stable dedupe key for event kinds Meta gives no natural message id for: a genuine
    /// redelivery of the identical webhook resends byte-identical JSON, so hashing the raw
    /// fragment (scoped by <paramref name="discriminator"/> = the webhook field name) is a
    /// robust, generic dedupe key without hand-picking fields per event kind.
    /// </summary>
    private static string HashKey(string discriminator, JsonElement value)
    {
        var raw = discriminator + "|" + value.GetRawText();
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(raw));
        return Convert.ToHexString(hash); // 64 hex chars — well within webhook_dedupe.wamid(128)
    }

    private static bool TryGetArray(JsonElement element, string propertyName, out JsonElement.ArrayEnumerator array)
    {
        if (element.ValueKind == JsonValueKind.Object
            && element.TryGetProperty(propertyName, out var prop)
            && prop.ValueKind == JsonValueKind.Array)
        {
            array = prop.EnumerateArray();
            return true;
        }

        array = default;
        return false;
    }

    private static string? GetString(JsonElement element, params ReadOnlySpan<string> path)
    {
        var current = element;
        foreach (var segment in path)
        {
            if (current.ValueKind != JsonValueKind.Object || !current.TryGetProperty(segment, out current))
                return null;
        }

        return current.ValueKind == JsonValueKind.String ? current.GetString() : null;
    }

    private static DateTimeOffset? ParseUnixSeconds(string? value)
    {
        if (value is null) return null;
        return long.TryParse(value, out var seconds) ? DateTimeOffset.FromUnixTimeSeconds(seconds) : null;
    }
}
