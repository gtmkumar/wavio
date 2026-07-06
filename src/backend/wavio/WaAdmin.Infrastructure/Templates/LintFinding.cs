namespace WaAdmin.Infrastructure.Templates;

/// <summary>
/// One lint finding, shared by <see cref="RulesTemplateLintService"/> and
/// <see cref="LlmTemplateLintService"/> so both serialize the same JSON shape into
/// templates.template_lint_results.findings (issue #27).
/// </summary>
/// <param name="Code">Machine-readable rule/finding code, e.g. UTILITY_PROMOTIONAL_LANGUAGE.</param>
/// <param name="Severity">error | warning | info. Only "error" blocks submission
/// (<see cref="TemplateLintOutcome.Passed"/> = false) — see each linter's own doc comment.</param>
/// <param name="Message">Human-readable explanation of the finding. May embed tenant-authored
/// template text (rules linter) or LLM-generated text — UNTRUSTED at render time: clients must
/// render it as plain text, never as HTML/markup (stored-XSS-at-render; issue #27 security
/// review, finding 4).</param>
/// <param name="SuggestedFix">Optional actionable fix suggestion. Same render-as-plain-text
/// contract as <paramref name="Message"/>.</param>
internal sealed record LintFinding(string Code, string Severity, string Message, string? SuggestedFix = null);
