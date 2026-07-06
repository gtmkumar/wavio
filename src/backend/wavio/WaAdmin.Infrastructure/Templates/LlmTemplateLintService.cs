using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using WaAdmin.Application.Common.Interfaces;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace WaAdmin.Infrastructure.Templates;

/// <summary>
/// Wave 3 optional second lint pass (Linter = "llm", issue #27, spec §4.4): asks the Anthropic
/// Messages API for a structured verdict (pass/fail, findings, suggested fixes) a static ruleset
/// can't produce — nuance like "this reads as promotional even though no banned phrase matched".
/// Registered in WaAdmin.Infrastructure's DependencyInjection only when
/// <see cref="LintLlmOptions.Enabled"/> is true, so when disabled/unconfigured the submission
/// pipeline runs rules-only (see <see cref="RulesTemplateLintService"/>) — this type is simply
/// absent from the <c>IEnumerable&lt;ITemplateLintService&gt;</c>
/// <see cref="WaAdmin.Application.Templates.TemplateSubmissionService"/> iterates.
///
/// Uses <c>output_config.format</c> (structured outputs, GA on Claude Opus 4.8 — see
/// <see cref="LintLlmOptions.Model"/>'s default) to constrain the response to a JSON schema
/// matching <see cref="LintFinding"/>, so a successful call is guaranteed valid JSON — no
/// markdown-fence stripping, no "hope it followed instructions".
///
/// Failure semantics (documented per issue #27's instruction to pick and document a policy):
/// ANY transport failure, non-2xx response, or unparseable envelope degrades to a non-blocking
/// "skipped" outcome (<see cref="TemplateLintOutcome.Passed"/> = true, one "info"-severity
/// finding recording why) — never blocks or crashes submission on an external dependency being
/// unavailable, mirroring <see cref="WaAdmin.Application.Templates.TemplateSubmissionService"/>'s
/// own never-block posture on the Meta Graph API call. Only the model's own verdict can produce a
/// blocking ("error"-severity) finding.
/// </summary>
public sealed partial class LlmTemplateLintService : ITemplateLintService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private const string VerdictSchemaJson = """
        {
            "type": "object",
            "properties": {
                "passed": { "type": "boolean" },
                "score": { "type": "integer" },
                "findings": {
                    "type": "array",
                    "items": {
                        "type": "object",
                        "properties": {
                            "code": { "type": "string" },
                            "severity": { "type": "string", "enum": ["error", "warning", "info"] },
                            "message": { "type": "string" },
                            "suggestedFix": { "type": "string" }
                        },
                        "required": ["code", "severity", "message"],
                        "additionalProperties": false
                    }
                }
            },
            "required": ["passed", "findings"],
            "additionalProperties": false
        }
        """;

    private readonly HttpClient _http;
    private readonly LintLlmOptions _options;
    private readonly ILogger<LlmTemplateLintService> _logger;

    public LlmTemplateLintService(HttpClient http, IOptions<LintLlmOptions> options, ILogger<LlmTemplateLintService> logger)
    {
        _http = http;
        _options = options.Value;
        _logger = logger;
    }

    public string Linter => "llm";

    public async Task<TemplateLintOutcome> LintAsync(TemplateLintInput input, CancellationToken cancellationToken)
    {
        // Defensive: DI only registers this service when Enabled is true, but a future caller
        // constructing it directly (e.g. a test) should still get the documented degrade, not an
        // unauthenticated call to Anthropic.
        if (!_options.Enabled || string.IsNullOrWhiteSpace(_options.ApiKey))
        {
            // Normally unreachable in a hosted app (ValidateOnStart rejects enabled-but-keyless
            // config at boot), but if it IS reached the skip must be visible in logs, not only
            // in the persisted finding (issue #27 finding 1).
            LogSkippedUnconfigured(_logger);
            return SkippedOutcome("LLM lint is disabled or unconfigured.");
        }

        var requestBody = new JsonObject
        {
            ["model"] = _options.Model,
            ["max_tokens"] = 1024,
            ["system"] = SystemPrompt,
            ["messages"] = new JsonArray { new JsonObject { ["role"] = "user", ["content"] = BuildPrompt(input) } },
            ["output_config"] = new JsonObject
            {
                ["format"] = new JsonObject
                {
                    ["type"] = "json_schema",
                    ["schema"] = JsonNode.Parse(VerdictSchemaJson),
                },
            },
        };

        using var request = new HttpRequestMessage(HttpMethod.Post, "/v1/messages")
        {
            Content = JsonContent.Create(requestBody, options: JsonOptions),
        };
        request.Headers.Add("x-api-key", _options.ApiKey);
        request.Headers.Add("anthropic-version", "2023-06-01");

        string responseBody;
        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(TimeSpan.FromSeconds(_options.TimeoutSeconds));
            using var response = await _http.SendAsync(request, cts.Token);
            responseBody = await response.Content.ReadAsStringAsync(cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                LogTransportFailed(_logger, (int)response.StatusCode);
                return SkippedOutcome($"LLM lint call failed: HTTP {(int)response.StatusCode}.");
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            // Never let a transport failure crash submission — same never-block posture as
            // TemplateSubmissionService's own Graph-API catch. Log the exception TYPE only: the
            // message could echo request content in some client library edge cases, and this
            // method's inputs/outputs must never appear in logs (template content, API key).
            LogTransportException(_logger, ex.GetType().Name);
            return SkippedOutcome("LLM lint call failed (transport error).");
        }

        return ParseVerdict(responseBody);
    }

    private TemplateLintOutcome ParseVerdict(string responseBody)
    {
        try
        {
            var envelope = JsonNode.Parse(responseBody) as JsonObject;
            var stopReason = envelope?["stop_reason"]?.GetValue<string>();
            if (stopReason == "refusal")
            {
                LogRefused(_logger);
                return SkippedOutcome("LLM lint call was refused by the model's safety classifier.");
            }

            var verdictText = envelope?["content"]?.AsArray()
                .Select(b => b as JsonObject)
                .FirstOrDefault(b => b?["type"]?.GetValue<string>() == "text")
                ?["text"]?.GetValue<string>();

            if (verdictText is null || JsonNode.Parse(verdictText) is not JsonObject verdict)
            {
                LogParseFailed(_logger);
                return SkippedOutcome("LLM lint returned no parseable verdict.");
            }

            var passed = verdict["passed"]?.GetValue<bool>() ?? true;
            var findings = (verdict["findings"] as JsonArray ?? [])
                .Select(f => f as JsonObject)
                .Where(f => f is not null)
                .Select(f => new LintFinding(
                    f!["code"]?.GetValue<string>() ?? "LLM_FINDING",
                    f["severity"]?.GetValue<string>() ?? "warning",
                    f["message"]?.GetValue<string>() ?? "Unspecified LLM finding.",
                    f["suggestedFix"]?.GetValue<string>()))
                .ToList();

            short? score = verdict["score"] is { } scoreNode && scoreNode.GetValueKind() != JsonValueKind.Null
                ? (short)Math.Clamp(scoreNode.GetValue<int>(), 0, 100)
                : null;

            return new TemplateLintOutcome(passed, JsonSerializer.Serialize(findings, JsonOptions), score);
        }
        // Not just JsonException: valid-JSON-but-wrong-shape envelopes (e.g. a proxy returning
        // 200 with {"content":"maintenance"}) make AsArray()/GetValue<T>() throw
        // InvalidOperationException/FormatException — those must degrade to "skipped" exactly
        // like malformed JSON, or the documented fail-open contract turns into a submission-
        // blocking 500 (issue #27 finding 3).
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or FormatException)
        {
            LogParseFailed(_logger);
            return SkippedOutcome("LLM lint returned an unparseable response body.");
        }
    }

    private static TemplateLintOutcome SkippedOutcome(string reason) =>
        new(true, JsonSerializer.Serialize(new[] { new LintFinding("LLM_SKIPPED", "info", reason) }, JsonOptions), null);

    private static string BuildPrompt(TemplateLintInput input) =>
        $"""
        Review this WhatsApp message template for Meta policy compliance.

        Category: {input.Category}
        Language: {input.Language}
        Components (Meta component JSON): {input.ComponentsJson}

        Rules to check: promotional language in a utility-category template; a missing opt-out
        instruction in a marketing-category template; excessive or oddly-placed variable
        placeholders; formatting issues (shouting, excessive punctuation/emoji, malformed
        placeholders). Respond with your verdict per the required JSON schema only.
        """;

    private const string SystemPrompt =
        "You are a WhatsApp Business template policy reviewer for a messaging platform. " +
        "Evaluate templates strictly against Meta's category and formatting policies and " +
        "respond only with the structured verdict the schema requires.";

    [LoggerMessage(Level = LogLevel.Warning, Message = "LLM lint call failed: HTTP {StatusCode}")]
    private static partial void LogTransportFailed(ILogger logger, int statusCode);

    [LoggerMessage(Level = LogLevel.Warning, Message = "LLM lint call threw {ExceptionType}")]
    private static partial void LogTransportException(ILogger logger, string exceptionType);

    [LoggerMessage(Level = LogLevel.Information, Message = "LLM lint call was refused by the model's safety classifier")]
    private static partial void LogRefused(ILogger logger);

    [LoggerMessage(Level = LogLevel.Warning, Message = "LLM lint returned an unparseable verdict")]
    private static partial void LogParseFailed(ILogger logger);

    [LoggerMessage(Level = LogLevel.Warning, Message = "LLM lint skipped: disabled or no ApiKey configured")]
    private static partial void LogSkippedUnconfigured(ILogger logger);
}
