namespace WaAdmin.Infrastructure.Templates;

/// <summary>
/// Configuration for the optional LLM lint pass (<see cref="LlmTemplateLintService"/>, issue #27).
/// Bound from <c>Lint:Llm</c>. Disabled by default — unlike <c>MetaGraphOptions</c>'
/// "boot even without full config" Development-only fallback, disabled here is a legitimate
/// steady state in any environment: the pipeline runs rules-only until a tenant/ops decision
/// explicitly turns the LLM pass on. See WaAdmin.Infrastructure's DependencyInjection — the
/// service is only registered when <see cref="Enabled"/> is true.
/// </summary>
public sealed class LintLlmOptions
{
    public const string SectionName = "Lint:Llm";

    public bool Enabled { get; set; }

    /// <summary>Anthropic Messages API base URL, e.g. https://api.anthropic.com.</summary>
    public string BaseUrl { get; set; } = "https://api.anthropic.com";

    /// <summary>Never logged.</summary>
    public string ApiKey { get; set; } = string.Empty;

    public string Model { get; set; } = "claude-opus-4-8";

    public int TimeoutSeconds { get; set; } = 15;

    /// <summary>
    /// ValidateOnStart predicate (see WaAdmin.Infrastructure DependencyInjection): a disabled
    /// config is always valid; an enabled one must carry an ApiKey and an absolute https BaseUrl —
    /// anything else fails the host at boot rather than silently skipping every LLM lint pass.
    /// </summary>
    public static bool IsValid(LintLlmOptions options) =>
        !options.Enabled
        || (!string.IsNullOrWhiteSpace(options.ApiKey)
            && Uri.TryCreate(options.BaseUrl, UriKind.Absolute, out var baseUri)
            && baseUri.Scheme == Uri.UriSchemeHttps);
}
