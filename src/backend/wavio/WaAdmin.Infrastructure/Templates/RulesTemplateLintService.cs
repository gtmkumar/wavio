using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using WaAdmin.Application.Common.Interfaces;

namespace WaAdmin.Infrastructure.Templates;

/// <summary>
/// Wave 3 static-ruleset linter (Linter = "rules", issue #27, spec §4.4). Replaces the always-pass
/// stub in the submission pipeline (<see cref="WaAdmin.Application.Templates.TemplateSubmissionService"/>
/// runs every registered <see cref="ITemplateLintService"/>). Deliberately dumb pattern/heuristic
/// checks, not an LLM call — <see cref="LlmTemplateLintService"/> is the second, optional pass for
/// nuance a static ruleset can't catch.
///
/// Checks (all "error" severity — a hit blocks submission, matching the issue's acceptance
/// criterion that known-bad fixtures are blocked pre-submission):
/// <list type="bullet">
/// <item>Promotional language in a utility-category template.</item>
/// <item>Missing opt-out instruction in a marketing-category template.</item>
/// <item>Variable-density: a placeholder-heavy component, or one that starts/ends with a
/// placeholder (Meta policy: static text must anchor both ends).</item>
/// <item>Formatting: empty body, all-caps shouting, excessive punctuation/emoji, malformed
/// placeholder syntax (anything other than numeric {{n}}).</item>
/// </list>
/// This is a v1 ruleset, not the full Meta policy surface — false negatives on cases the spec
/// doesn't enumerate are expected; false positives on the seeded vertical packs are not (see
/// <see cref="VerticalTemplatePacks"/> and its lint-clean test).
/// </summary>
public sealed class RulesTemplateLintService : ITemplateLintService
{
    public string Linter => "rules";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    // Meta's own guidance + spec §4.4 examples. Deliberately a flat phrase list, not NLP — a
    // vertical author can always trip this on a legitimate utility message that happens to
    // mention "free" (e.g. "toll-free number"); that's an accepted false-positive rate for v1.
    private static readonly string[] PromotionalPhrases =
    [
        "discount", "% off", "percent off", "sale", "special offer", "limited time",
        "limited-time", "hurry", "act now", "deal", "deals", "clearance", "coupon",
        "promo code", "free gift", "buy now", "while supplies last", "flash sale",
        "lowest price", "best price", "exclusive offer",
    ];

    private static readonly string[] OptOutPhrases =
    [
        "reply stop", "text stop", "reply \"stop\"", "opt out", "opt-out", "unsubscribe",
        "stop promotions", "stop promotion",
    ];

    private static readonly Regex PlaceholderRegex = new(@"\{\{([^{}]*)\}\}", RegexOptions.Compiled);
    private static readonly Regex ExcessivePunctuationRegex = new(@"[!?]{3,}", RegexOptions.Compiled);

    public Task<TemplateLintOutcome> LintAsync(TemplateLintInput input, CancellationToken cancellationToken)
    {
        var findings = new List<LintFinding>();
        var components = JsonNode.Parse(input.ComponentsJson) as JsonArray ?? [];

        var bodyText = TextOf(components, "BODY");
        var headerText = TextOf(components, "HEADER");
        var footerText = TextOf(components, "FOOTER");
        var buttonTexts = ButtonTexts(components).ToList();

        if (string.IsNullOrWhiteSpace(bodyText))
        {
            findings.Add(new LintFinding("FORMATTING_EMPTY_BODY", "error",
                "Template has no BODY text.", "Add a non-empty BODY component."));
        }

        var allStaticText = string.Join(
            " ", new[] { headerText, bodyText, footerText }.Where(t => !string.IsNullOrEmpty(t)));

        if (string.Equals(input.Category, "utility", StringComparison.OrdinalIgnoreCase))
        {
            var hit = PromotionalPhrases.FirstOrDefault(
                p => allStaticText.Contains(p, StringComparison.OrdinalIgnoreCase));
            if (hit is not null)
            {
                findings.Add(new LintFinding("UTILITY_PROMOTIONAL_LANGUAGE", "error",
                    $"Utility template contains promotional language ('{hit}').",
                    "Remove promotional phrasing, or recategorize as marketing."));
            }
        }

        if (string.Equals(input.Category, "marketing", StringComparison.OrdinalIgnoreCase))
        {
            var hasOptOut = OptOutPhrases.Any(p => allStaticText.Contains(p, StringComparison.OrdinalIgnoreCase))
                || buttonTexts.Any(b => OptOutPhrases.Any(p => b.Contains(p, StringComparison.OrdinalIgnoreCase)));
            if (!hasOptOut)
            {
                findings.Add(new LintFinding("MARKETING_MISSING_OPT_OUT", "error",
                    "Marketing template has no opt-out instruction.",
                    "Add a footer or quick-reply button with an opt-out instruction (e.g. \"Reply STOP to opt out\")."));
            }
        }

        foreach (var (type, text) in components.OfType<JsonObject>().Select(TypeAndText))
        {
            if (string.IsNullOrEmpty(text)) continue;
            LintPlaceholders(type, text, findings);
            LintFormatting(type, text, findings);
        }

        var passed = findings.All(f => f.Severity != "error");
        var score = ComputeScore(findings);
        return Task.FromResult(new TemplateLintOutcome(passed, JsonSerializer.Serialize(findings, JsonOptions), score));
    }

    private static (string Type, string? Text) TypeAndText(JsonObject node) =>
        ((node["type"]?.GetValue<string>() ?? string.Empty).ToUpperInvariant(), node["text"]?.GetValue<string>());

    private static string? TextOf(JsonArray components, string type) =>
        components.OfType<JsonObject>()
            .Select(TypeAndText)
            .FirstOrDefault(c => c.Type == type)
            .Text;

    private static IEnumerable<string> ButtonTexts(JsonArray components)
    {
        foreach (var node in components.OfType<JsonObject>())
        {
            if ((node["type"]?.GetValue<string>() ?? string.Empty).ToUpperInvariant() != "BUTTONS") continue;
            if (node["buttons"] is not JsonArray buttons) continue;
            foreach (var button in buttons.OfType<JsonObject>())
            {
                if (button["text"]?.GetValue<string>() is { } text)
                    yield return text;
            }
        }
    }

    private static void LintPlaceholders(string type, string text, List<LintFinding> findings)
    {
        var matches = PlaceholderRegex.Matches(text);

        foreach (Match match in matches)
        {
            if (int.TryParse(match.Groups[1].Value, out _)) continue;
            // Truncated: match.Value is tenant-controlled and otherwise unbounded — finding
            // messages are stored and later rendered by clients (issue #27 finding 4).
            findings.Add(new LintFinding("FORMATTING_MALFORMED_PLACEHOLDER", "error",
                $"{type} contains a malformed placeholder '{Truncate(match.Value, 100)}'.",
                "Placeholders must be numeric, e.g. {{1}}."));
            break; // one finding per component is enough signal
        }

        // Anything left over after stripping valid {{n}} pairs is a stray/single brace, e.g. "{1}".
        var withoutValidPlaceholders = PlaceholderRegex.Replace(text, string.Empty);
        if (withoutValidPlaceholders.Contains('{') || withoutValidPlaceholders.Contains('}'))
        {
            findings.Add(new LintFinding("FORMATTING_MALFORMED_PLACEHOLDER", "error",
                $"{type} contains malformed brace syntax.",
                "Use double-brace numeric placeholders, e.g. {{1}}."));
        }

        if (matches.Count == 0) return;

        var trimmed = text.Trim();
        if (trimmed.StartsWith("{{", StringComparison.Ordinal) || trimmed.EndsWith("}}", StringComparison.Ordinal))
        {
            findings.Add(new LintFinding("VARIABLE_LEADING_OR_TRAILING", "error",
                $"{type} text starts or ends with a variable.",
                "Add static text before the first and after the last placeholder."));
        }

        var wordCount = Math.Max(text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Length, 1);
        if (matches.Count > 4 && matches.Count * 3 >= wordCount)
        {
            findings.Add(new LintFinding("VARIABLE_DENSITY_TOO_HIGH", "error",
                $"{type} has {matches.Count} placeholders relative to {wordCount} words — too variable-dense.",
                "Reduce the number of variables or add more static context text."));
        }
    }

    private static void LintFormatting(string type, string text, List<LintFinding> findings)
    {
        var letters = text.Where(char.IsLetter).ToList();
        if (letters.Count >= 8)
        {
            var upperRatio = letters.Count(char.IsUpper) / (double)letters.Count;
            if (upperRatio >= 0.7)
            {
                findings.Add(new LintFinding("FORMATTING_ALL_CAPS", "error",
                    $"{type} text is mostly uppercase ({upperRatio:P0}).",
                    "Use normal sentence casing instead of all-caps."));
            }
        }

        if (ExcessivePunctuationRegex.IsMatch(text))
        {
            findings.Add(new LintFinding("FORMATTING_EXCESSIVE_PUNCTUATION_OR_EMOJI", "error",
                $"{type} text has excessive repeated punctuation.",
                "Limit to a single punctuation mark."));
        }
        else if (CountEmoji(text) >= 3)
        {
            findings.Add(new LintFinding("FORMATTING_EXCESSIVE_PUNCTUATION_OR_EMOJI", "error",
                $"{type} text has excessive emoji.",
                "Limit to at most one or two emoji."));
        }
    }

    private static int CountEmoji(string text)
    {
        var count = 0;
        for (var i = 0; i < text.Length; i++)
        {
            int codepoint;
            if (char.IsHighSurrogate(text[i]) && i + 1 < text.Length && char.IsLowSurrogate(text[i + 1]))
            {
                codepoint = char.ConvertToUtf32(text[i], text[i + 1]);
                i++;
            }
            else
            {
                codepoint = text[i];
            }

            if ((codepoint >= 0x1F300 && codepoint <= 0x1FAFF) || (codepoint >= 0x2600 && codepoint <= 0x27BF))
                count++;
        }
        return count;
    }

    private static string Truncate(string value, int maxLength) =>
        value.Length <= maxLength ? value : value[..maxLength] + "…";

    private static short ComputeScore(List<LintFinding> findings)
    {
        var errors = findings.Count(f => f.Severity == "error");
        return (short)Math.Clamp(100 - (errors * 20), 0, 100);
    }
}
