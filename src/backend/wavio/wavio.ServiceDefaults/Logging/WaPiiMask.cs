using System.Text.RegularExpressions;
using Serilog.Core;
using Serilog.Events;

namespace wavio.ServiceDefaults.Logging;

/// <summary>
/// wa_id masking for logs (spec §5: wa_id is personal data — masked in logs).
///
/// Three enforcement layers:
///   1. <see cref="MaskWaId"/> — call sites mask explicitly when interpolating a wa_id
///      into a message.
///   2. <see cref="WaIdMaskingEnricher"/> — safety net that rewrites any structured log
///      property whose name identifies it as a wa_id, so a forgotten call site still
///      never emits the full value.
///   3. <see cref="MaskDigitRunsInPath"/> — path/URL-shaped values (request paths, OTel
///      <c>url.path</c>) can't be exact-matched against a property name (they're a mix of
///      route text and a parameter value), so this masks any embedded wa_id-length digit
///      run instead of the whole value. Found live (issue #15 security review, S2): a raw
///      wa_id in <c>GET /v1/windows/{waId}</c>'s path landed unmasked in ASP.NET Core's own
///      request-start/request-finished logs and OTel spans, since neither goes through
///      layer 1 or 2 — this is that gap's fix, applied to the "Path" log property (see
///      <see cref="WaIdMaskingEnricher"/>) and to OTel's <c>url.path</c> tag (see
///      wavio.ServiceDefaults/Extensions.cs's <c>EnrichWithHttpRequest</c> callback).
///
/// Message BODIES never reach logs or traces at all — do not add body properties to log
/// events or OTel span attributes anywhere.
/// </summary>
public static partial class WaPiiMask
{
    /// <summary>
    /// Masks a wa_id (E.164 digits, no '+'): all but the last 4 digits replaced with '•'
    /// (e.g. <c>919812345678</c> → <c>••••••••5678</c>). Same convention as the
    /// financial-PII maskers in wavio.SharedDataModel.
    /// </summary>
    public static string MaskWaId(string? waId)
    {
        if (string.IsNullOrEmpty(waId)) return string.Empty;
        if (waId.Length <= 4) return new string('•', waId.Length);
        return new string('•', waId.Length - 4) + waId[^4..];
    }

    // E.164 wa_ids are 10-15 digits with no separators — long enough that no route segment,
    // resource id fragment, or port number plausibly collides with this length in practice.
    [GeneratedRegex(@"(?<!\d)\d{10,15}(?!\d)")]
    private static partial Regex DigitRunPattern();

    /// <summary>
    /// Masks every embedded 10-15-digit run within <paramref name="text"/> (e.g. a request path
    /// like <c>/api/v1/windows/919876543210</c> → <c>/api/v1/windows/••••••••3210</c>), leaving
    /// everything else — route text, other numeric ids of a different length — untouched. Use
    /// for free-text values that CONTAIN a wa_id rather than values that ARE one; for the latter,
    /// use <see cref="MaskWaId"/> directly.
    /// </summary>
    public static string MaskDigitRunsInPath(string? text)
    {
        if (string.IsNullOrEmpty(text)) return string.Empty;
        return DigitRunPattern().Replace(text, m => MaskWaId(m.Value));
    }
}

/// <summary>
/// Serilog enricher that masks any log-event property carrying a wa_id, regardless of how
/// it got there (structured logging, LogContext, destructured objects one level deep is NOT
/// covered — never destructure customer objects into logs).
/// </summary>
public sealed class WaIdMaskingEnricher : ILogEventEnricher
{
    // Property names treated as wa_id carriers (case-insensitive): the WHOLE value IS a wa_id.
    private static readonly HashSet<string> WaIdPropertyNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "WaId", "wa_id", "CustomerWaId", "RecipientWaId", "SenderWaId", "To",
    };

    // Property names treated as PATH carriers (case-insensitive): the value is free text that
    // may CONTAIN a wa_id among other route/query text — e.g. ASP.NET Core's own
    // "Request starting {Protocol} {Method} {Scheme}://{Host}{PathBase}{Path}{QueryString}"
    // request-lifecycle logs, which are outside application code entirely and so can't be fixed
    // at a call site (issue #15 security review, S2).
    private static readonly HashSet<string> PathPropertyNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "Path", "RequestPath", "QueryString",
    };

    public void Enrich(LogEvent logEvent, ILogEventPropertyFactory propertyFactory)
    {
        List<(string Name, string Masked)>? replacements = null;

        foreach (var (name, value) in logEvent.Properties)
        {
            if (value is not ScalarValue { Value: string s } || s.Length == 0) continue;

            if (PathPropertyNames.Contains(name))
            {
                var pathMasked = WaPiiMask.MaskDigitRunsInPath(s);
                if (pathMasked != s)
                    (replacements ??= []).Add((name, pathMasked));
                continue;
            }

            if (!WaIdPropertyNames.Contains(name)) continue;

            // Only rewrite values that look like a wa_id (all digits) — "To" in particular
            // is a common property name; an email or URL under it is not a wa_id.
            if (name.Equals("To", StringComparison.OrdinalIgnoreCase) && !s.All(char.IsAsciiDigit))
                continue;

            (replacements ??= []).Add((name, WaPiiMask.MaskWaId(s)));
        }

        if (replacements is null) return;

        foreach (var (name, masked) in replacements)
            logEvent.AddOrUpdateProperty(propertyFactory.CreateProperty(name, masked));
    }
}
