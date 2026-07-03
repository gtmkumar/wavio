namespace WaGateway.Application.Messages.Dtos;

/// <summary>
/// Typed payload shapes for <c>POST /v1/messages</c> (spec §4.2) — one record per
/// <c>message_type</c>, closely mirroring Meta's WhatsApp Cloud API message object shapes but
/// only as deep as Wave 1 needs (not a full re-implementation of Meta's schema). Deserialized
/// from the request's raw JSON payload based on the declared <c>MessageType</c> — see
/// <see cref="WaGateway.Application.Messages.Logic.MessagePayloadValidator"/> for the
/// per-type structural validation and <c>MessageTypes"</c> for the canonical type-string catalog.
/// </summary>
public static class MessageTypes
{
    public const string Text = "text";
    public const string Template = "template";
    public const string Media = "media";
    public const string InteractiveButtons = "interactive_buttons";
    public const string InteractiveList = "interactive_list";
    public const string InteractiveCtaUrl = "interactive_cta_url";
    public const string InteractiveFlow = "interactive_flow";
    public const string Location = "location";
    public const string Contacts = "contacts";
    public const string Reaction = "reaction";
    public const string OrderDetails = "order_details";

    public static readonly IReadOnlyCollection<string> All =
    [
        Text, Template, Media, InteractiveButtons, InteractiveList, InteractiveCtaUrl,
        InteractiveFlow, Location, Contacts, Reaction, OrderDetails
    ];

    /// <summary>Only <see cref="Template"/> sends carry a Meta-approved template — every other
    /// type is a free-form send and is subject to the window-aware send policy (ADR-005).</summary>
    public static bool IsFreeForm(string messageType) => messageType != Template;
}

public sealed record TextPayload(string Body, bool? PreviewUrl);

/// <summary>
/// <see cref="Category"/> is declared by the caller rather than looked up from wa-admin-svc's
/// template catalog (issue #16) — a deliberate Wave 1 scope cut to avoid a cross-service
/// dependency for this issue; see the issue #14 decisions memory.
/// </summary>
public sealed record TemplatePayload(string Name, string Language, string Category, string? ComponentsJson);

public sealed record MediaPayload(string MediaType, string? MediaId, string? Link, string? Caption, string? Filename);

public sealed record InteractiveButton(string Id, string Title);
public sealed record InteractiveButtonsPayload(string BodyText, IReadOnlyList<InteractiveButton> Buttons);

public sealed record InteractiveListRow(string Id, string Title, string? Description);
public sealed record InteractiveListSection(string Title, IReadOnlyList<InteractiveListRow> Rows);
public sealed record InteractiveListPayload(string BodyText, string ButtonText, IReadOnlyList<InteractiveListSection> Sections);

public sealed record InteractiveCtaUrlPayload(string BodyText, string DisplayText, string Url);

public sealed record InteractiveFlowPayload(string BodyText, string FlowId, string FlowCta, string? FlowToken);

public sealed record LocationPayload(double Latitude, double Longitude, string? Name, string? Address);

public sealed record Contact(string FormattedName, string? PhoneE164);
public sealed record ContactsPayload(IReadOnlyList<Contact> Contacts);

/// <summary><see cref="MessageId"/> is the wamid of the message being reacted to.</summary>
public sealed record ReactionPayload(string MessageId, string Emoji);

public sealed record OrderDetailsPayload(string ReferenceId, decimal Amount, string CurrencyCode, string? Description);
