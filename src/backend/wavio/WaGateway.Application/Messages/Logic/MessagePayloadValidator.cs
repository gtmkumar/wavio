using System.Text.Json;
using WaGateway.Application.Messages.Dtos;

namespace WaGateway.Application.Messages.Logic;

/// <summary>
/// Structural, per-message-type validation of the raw JSON payload (spec §4.2 "typed payloads").
/// Deliberately shallow — required-field presence and basic shape, not a full re-implementation
/// of Meta's Cloud API validation (Meta itself is the final authority and will reject a
/// malformed send at the Graph boundary; this is a fast, local first line of defense). Pure and
/// exception-safe: malformed JSON or an unrecognized type both return validation errors rather
/// than throwing, so the caller always gets a normal 400 rather than a 500.
/// </summary>
public static class MessagePayloadValidator
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static IReadOnlyList<string> Validate(string messageType, string payloadJson)
    {
        try
        {
            return messageType switch
            {
                MessageTypes.Text => ValidateText(payloadJson),
                MessageTypes.Template => ValidateTemplate(payloadJson),
                MessageTypes.Media => ValidateMedia(payloadJson),
                MessageTypes.InteractiveButtons => ValidateInteractiveButtons(payloadJson),
                MessageTypes.InteractiveList => ValidateInteractiveList(payloadJson),
                MessageTypes.InteractiveCtaUrl => ValidateInteractiveCtaUrl(payloadJson),
                MessageTypes.InteractiveFlow => ValidateInteractiveFlow(payloadJson),
                MessageTypes.Location => ValidateLocation(payloadJson),
                MessageTypes.Contacts => ValidateContacts(payloadJson),
                MessageTypes.Reaction => ValidateReaction(payloadJson),
                MessageTypes.OrderDetails => ValidateOrderDetails(payloadJson),
                _ => [$"Unknown message type '{messageType}'. Must be one of: {string.Join(", ", MessageTypes.All)}."]
            };
        }
        catch (JsonException)
        {
            return ["Payload is not valid JSON."];
        }
    }

    private static IReadOnlyList<string> ValidateText(string json)
    {
        var payload = JsonSerializer.Deserialize<TextPayload>(json, JsonOptions);
        return string.IsNullOrWhiteSpace(payload?.Body) ? ["text.body is required."] : [];
    }

    private static IReadOnlyList<string> ValidateTemplate(string json)
    {
        var payload = JsonSerializer.Deserialize<TemplatePayload>(json, JsonOptions);
        var errors = new List<string>();
        if (string.IsNullOrWhiteSpace(payload?.Name)) errors.Add("template.name is required.");
        if (string.IsNullOrWhiteSpace(payload?.Language)) errors.Add("template.language is required.");
        if (payload?.Category is not ("utility" or "marketing" or "authentication"))
            errors.Add("template.category must be utility, marketing, or authentication.");
        return errors;
    }

    private static IReadOnlyList<string> ValidateMedia(string json)
    {
        var payload = JsonSerializer.Deserialize<MediaPayload>(json, JsonOptions);
        var errors = new List<string>();
        if (payload?.MediaType is not ("image" or "video" or "audio" or "document" or "sticker"))
            errors.Add("media.mediaType must be image, video, audio, document, or sticker.");
        if (string.IsNullOrWhiteSpace(payload?.MediaId) && string.IsNullOrWhiteSpace(payload?.Link))
            errors.Add("media requires either mediaId or link.");
        return errors;
    }

    private static IReadOnlyList<string> ValidateInteractiveButtons(string json)
    {
        var payload = JsonSerializer.Deserialize<InteractiveButtonsPayload>(json, JsonOptions);
        var errors = new List<string>();
        if (string.IsNullOrWhiteSpace(payload?.BodyText)) errors.Add("interactive_buttons.bodyText is required.");
        if (payload?.Buttons is not { Count: > 0 and <= 3 })
            errors.Add("interactive_buttons.buttons must have 1-3 entries (Meta limit).");
        return errors;
    }

    private static IReadOnlyList<string> ValidateInteractiveList(string json)
    {
        var payload = JsonSerializer.Deserialize<InteractiveListPayload>(json, JsonOptions);
        var errors = new List<string>();
        if (string.IsNullOrWhiteSpace(payload?.BodyText)) errors.Add("interactive_list.bodyText is required.");
        if (string.IsNullOrWhiteSpace(payload?.ButtonText)) errors.Add("interactive_list.buttonText is required.");
        if (payload?.Sections is not { Count: > 0 }) errors.Add("interactive_list.sections must have at least 1 entry.");
        return errors;
    }

    private static IReadOnlyList<string> ValidateInteractiveCtaUrl(string json)
    {
        var payload = JsonSerializer.Deserialize<InteractiveCtaUrlPayload>(json, JsonOptions);
        var errors = new List<string>();
        if (string.IsNullOrWhiteSpace(payload?.BodyText)) errors.Add("interactive_cta_url.bodyText is required.");
        if (string.IsNullOrWhiteSpace(payload?.Url)) errors.Add("interactive_cta_url.url is required.");
        return errors;
    }

    private static IReadOnlyList<string> ValidateInteractiveFlow(string json)
    {
        var payload = JsonSerializer.Deserialize<InteractiveFlowPayload>(json, JsonOptions);
        var errors = new List<string>();
        if (string.IsNullOrWhiteSpace(payload?.BodyText)) errors.Add("interactive_flow.bodyText is required.");
        if (string.IsNullOrWhiteSpace(payload?.FlowId)) errors.Add("interactive_flow.flowId is required.");
        if (string.IsNullOrWhiteSpace(payload?.FlowCta)) errors.Add("interactive_flow.flowCta is required.");
        return errors;
    }

    private static IReadOnlyList<string> ValidateLocation(string json)
    {
        var payload = JsonSerializer.Deserialize<LocationPayload>(json, JsonOptions);
        var errors = new List<string>();
        if (payload is null || payload.Latitude is < -90 or > 90) errors.Add("location.latitude must be between -90 and 90.");
        if (payload is null || payload.Longitude is < -180 or > 180) errors.Add("location.longitude must be between -180 and 180.");
        return errors;
    }

    private static IReadOnlyList<string> ValidateContacts(string json)
    {
        var payload = JsonSerializer.Deserialize<ContactsPayload>(json, JsonOptions);
        return payload?.Contacts is not { Count: > 0 } ? ["contacts.contacts must have at least 1 entry."] : [];
    }

    private static IReadOnlyList<string> ValidateReaction(string json)
    {
        var payload = JsonSerializer.Deserialize<ReactionPayload>(json, JsonOptions);
        var errors = new List<string>();
        if (string.IsNullOrWhiteSpace(payload?.MessageId)) errors.Add("reaction.messageId is required.");
        // Empty emoji is Meta's own documented way to REMOVE a reaction — allowed, not an error.
        return errors;
    }

    private static IReadOnlyList<string> ValidateOrderDetails(string json)
    {
        var payload = JsonSerializer.Deserialize<OrderDetailsPayload>(json, JsonOptions);
        var errors = new List<string>();
        if (string.IsNullOrWhiteSpace(payload?.ReferenceId)) errors.Add("order_details.referenceId is required.");
        if (payload is null || payload.Amount <= 0) errors.Add("order_details.amount must be positive.");
        if (string.IsNullOrWhiteSpace(payload?.CurrencyCode)) errors.Add("order_details.currencyCode is required.");
        return errors;
    }
}
