using Serilog.Core;
using Serilog.Events;

namespace wavio.ServiceDefaults.Logging;

/// <summary>
/// wa_id masking for logs (spec §5: wa_id is personal data — masked in logs).
///
/// Two enforcement layers:
///   1. <see cref="MaskWaId"/> — call sites mask explicitly when interpolating a wa_id
///      into a message.
///   2. <see cref="WaIdMaskingEnricher"/> — safety net that rewrites any structured log
///      property whose name identifies it as a wa_id, so a forgotten call site still
///      never emits the full value.
///
/// Message BODIES never reach logs or traces at all — do not add body properties to log
/// events or OTel span attributes anywhere.
/// </summary>
public static class WaPiiMask
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
}

/// <summary>
/// Serilog enricher that masks any log-event property carrying a wa_id, regardless of how
/// it got there (structured logging, LogContext, destructured objects one level deep is NOT
/// covered — never destructure customer objects into logs).
/// </summary>
public sealed class WaIdMaskingEnricher : ILogEventEnricher
{
    // Property names treated as wa_id carriers (case-insensitive).
    private static readonly HashSet<string> WaIdPropertyNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "WaId", "wa_id", "CustomerWaId", "RecipientWaId", "SenderWaId", "To",
    };

    public void Enrich(LogEvent logEvent, ILogEventPropertyFactory propertyFactory)
    {
        List<(string Name, string Masked)>? replacements = null;

        foreach (var (name, value) in logEvent.Properties)
        {
            if (!WaIdPropertyNames.Contains(name)) continue;
            if (value is not ScalarValue { Value: string s } || s.Length == 0) continue;

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
