namespace WaIngest.Application.Common.Options;

/// <summary>
/// Meta webhook receiver configuration (spec §4.3, §5). Bound from config section
/// <c>Meta:Webhook</c>. Both values are secrets: never logged, never returned in a response body.
///
/// Startup posture mirrors the shared PII-key pattern (wavio.SharedDataModel/DependencyInjection):
/// Development falls back to a fixed, clearly-labelled dev value when unset; every other
/// environment fails closed at startup. See WaIngest.WebApi/Program.cs.
/// </summary>
public sealed class MetaWebhookOptions
{
    public const string SectionName = "Meta:Webhook";

    /// <summary>Meta App Secret — HMAC-SHA256 key for the <c>X-Hub-Signature-256</c> header.</summary>
    public string AppSecret { get; set; } = string.Empty;

    /// <summary>Shared secret Meta echoes back during the GET subscription handshake
    /// (<c>hub.verify_token</c>) — configured by us in the Meta App dashboard.</summary>
    public string VerifyToken { get; set; } = string.Empty;
}
