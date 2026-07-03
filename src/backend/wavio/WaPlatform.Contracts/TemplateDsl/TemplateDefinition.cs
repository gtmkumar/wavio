namespace WaPlatform.Contracts.TemplateDsl;

/// <summary>
/// Platform-native template DSL (stub for #8) — the vertical-facing way to define WhatsApp
/// message templates without knowing Meta's component JSON. wa-admin-svc compiles this to
/// the Graph API template payload on submit, and the lint pipeline (Wave 1 stub #16,
/// full rules Wave 3 #27) validates it before submission.
/// Additive-only: extend with new component kinds; never repurpose existing ones.
/// </summary>
public sealed record TemplateDefinition
{
    /// <summary>Meta template name (lowercase snake_case, unique per WABA + language).</summary>
    public required string Name { get; init; }

    /// <summary>BCP-47 / Meta language code (e.g. en, en_US, hi).</summary>
    public required string Language { get; init; }

    public required TemplateCategory Category { get; init; }

    public required IReadOnlyList<TemplateComponent> Components { get; init; }
}

/// <summary>
/// Meta template category — determines the per-message price (PMP) and the lint rules that
/// apply. Category integrity is a Meta policy obligation (spec §9): miscategorized templates
/// get rejected or recategorized.
/// </summary>
public enum TemplateCategory
{
    Marketing,
    Utility,
    Authentication,
}

/// <summary>One template component: header, body, footer, or button block.</summary>
public sealed record TemplateComponent
{
    /// <summary>header | body | footer | buttons.</summary>
    public required string Type { get; init; }

    /// <summary>Literal text with {{n}} placeholders (header/body/footer).</summary>
    public string? Text { get; init; }

    /// <summary>
    /// Component-specific extras as a JSON document string (button definitions, header media
    /// format, …). Typed shapes land with the template lifecycle slices (#16).
    /// </summary>
    public string? ExtrasJson { get; init; }
}
