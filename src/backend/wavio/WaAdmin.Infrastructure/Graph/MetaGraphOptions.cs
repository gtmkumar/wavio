namespace WaAdmin.Infrastructure.Graph;

/// <summary>
/// Configuration for <see cref="MetaGraphTemplateClient"/> (issue #16 Task 2). Bound from
/// <c>Meta:Graph</c>. <see cref="AccessToken"/> is a single system-user token read from config —
/// real per-WABA, envelope-encrypted token storage arrives with onboarding (issue #6); this is
/// enough to exercise the full submit flow against a stub server in dev/tests.
/// </summary>
public sealed class MetaGraphOptions
{
    public const string SectionName = "Meta:Graph";

    /// <summary>e.g. https://graph.facebook.com in production, or the local stub server's URL.</summary>
    public string BaseUrl { get; set; } = string.Empty;

    /// <summary>e.g. "v21.0".</summary>
    public string ApiVersion { get; set; } = "v21.0";

    /// <summary>System-user bearer token. Never logged.</summary>
    public string AccessToken { get; set; } = string.Empty;

    /// <summary>Meta app id — used by the Embedded Signup code→token exchange (onboarding
    /// wizard). Empty in stub mode; the stub ignores client credentials.</summary>
    public string AppId { get; set; } = string.Empty;

    /// <summary>Meta app secret for the code→token exchange. Never logged.</summary>
    public string AppSecret { get; set; } = string.Empty;
}
