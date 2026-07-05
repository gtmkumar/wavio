using System.Globalization;
using System.Text.RegularExpressions;

namespace WaAdmin.Application.Consent.Logic;

/// <summary>
/// Detects an opt-out keyword in an inbound message body (spec §4.10: "STOP-keyword listener,
/// multi-language: EN/HI + per-vertical vocab"). Pure, no I/O — every case is unit-tested without
/// spinning up the RabbitMQ consumer.
///
/// Matching is TOKEN-based, not substring/regex: the message is split into whitespace-delimited
/// words and each vocabulary phrase's own words must appear as a contiguous run inside that
/// token list. This correctly matches "please STOP now" against the single-word phrase "stop"
/// and "band karo please" against the two-word phrase "band karo", while NOT matching an
/// unrelated word that merely CONTAINS the phrase as a substring (e.g. "stopwatch" must not
/// trigger on "stop") — a plain <c>Contains</c> check would false-positive on that.
///
/// Case-insensitive for Latin script (English + romanized Hindi); Devanagari has no case concept
/// so those phrases compare as-is after whitespace normalization.
/// </summary>
public static partial class OptOutKeywordMatcher
{
    /// <summary>Built-in vocabulary (spec §4.10's own examples): English, Devanagari Hindi, and
    /// romanized Hindi. Deliberately short and literal — extend via
    /// <see cref="TryMatch(string, IReadOnlyList{ValueTuple{string, string}})"/>'s
    /// <c>extraVocabulary</c> parameter for a per-vertical addition rather than editing this list,
    /// so a vertical's custom phrases don't require a platform code change.</summary>
    public static readonly IReadOnlyList<(string Phrase, string Language)> DefaultVocabulary =
    [
        ("stop", "en"),
        ("unsubscribe", "en"),
        ("opt out", "en"),
        ("cancel subscription", "en"),
        ("quit", "en"),
        ("बंद", "hi"),
        ("रोको", "hi"),
        ("बंद करो", "hi"),
        ("band", "hi-Latn"),
        ("band karo", "hi-Latn"),
        ("roko", "hi-Latn"),
    ];

    /// <summary>
    /// Returns the matched (Keyword, Language) for the best vocabulary phrase found in
    /// <paramref name="messageText"/>. Candidates are tried LONGEST-PHRASE-FIRST (by token
    /// count), not in list order: "band karo" and "band" are both in
    /// <see cref="DefaultVocabulary"/>, and "band karo" tokenizes to two words that both contain
    /// "band" as their first token — checking "band" first would always win and mask the more
    /// specific two-word phrase, which is never the intended match when the fuller phrase is
    /// actually present. <see cref="DefaultVocabulary"/> is concatenated before
    /// <paramref name="extraVocabulary"/> and LINQ's ordering is stable, so among phrases of EQUAL
    /// length the built-in vocabulary still wins a tie — a per-vertical addition can only ADD
    /// detections, never shadow a platform default. Returns null if nothing matches.
    /// </summary>
    public static (string Keyword, string Language)? TryMatch(
        string? messageText, IReadOnlyList<(string Phrase, string Language)>? extraVocabulary = null)
    {
        if (string.IsNullOrWhiteSpace(messageText)) return null;

        var tokens = Tokenize(messageText);
        if (tokens.Count == 0) return null;

        var candidates = DefaultVocabulary
            .Concat(extraVocabulary ?? [])
            .OrderByDescending(c => Tokenize(c.Phrase).Count);

        foreach (var (phrase, language) in candidates)
        {
            if (ContainsPhrase(tokens, phrase)) return (phrase, language);
        }

        return null;
    }

    /// <summary>Convenience boolean form for callers that only need the yes/no answer.</summary>
    public static bool IsOptOutKeyword(
        string? messageText, IReadOnlyList<(string Phrase, string Language)>? extraVocabulary = null) =>
        TryMatch(messageText, extraVocabulary) is not null;

    private static bool ContainsPhrase(List<string> tokens, string phrase)
    {
        var phraseTokens = Tokenize(phrase);
        if (phraseTokens.Count == 0 || phraseTokens.Count > tokens.Count) return false;

        for (var start = 0; start <= tokens.Count - phraseTokens.Count; start++)
        {
            var isMatch = true;
            for (var i = 0; i < phraseTokens.Count; i++)
            {
                if (tokens[start + i] != phraseTokens[i]) { isMatch = false; break; }
            }
            if (isMatch) return true;
        }
        return false;
    }

    /// <summary>Lowercases (Latin script only — Devanagari has no case) and splits on any run of
    /// non-letter characters (whitespace AND punctuation — "Band karo!" and "band, karo" both
    /// tokenize the same as "band karo"), dropping empty entries. <c>\p{L}</c> covers Devanagari
    /// as well as Latin, so a single tokenizer handles every vocabulary entry.</summary>
    private static List<string> Tokenize(string text) =>
        [.. NonLetterRunPattern().Split(text.ToLower(CultureInfo.InvariantCulture))
            .Where(t => t.Length > 0)];

    [GeneratedRegex(@"\P{L}+")]
    private static partial Regex NonLetterRunPattern();
}
