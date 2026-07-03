namespace WaAdmin.Application.Common.Interfaces;

/// <summary>Outcome of a lint run, persisted verbatim to templates.template_lint_results.</summary>
/// <param name="Passed">Whether the version may proceed to submission.</param>
/// <param name="Findings">JSON array of finding objects (jsonb) — empty for a clean pass.</param>
/// <param name="Score">Optional 0-100 quality score; null when the linter doesn't score.</param>
public sealed record TemplateLintOutcome(bool Passed, string Findings, short? Score);

/// <summary>
/// Lints a template version's content before submission (spec §4.4, issue #16 Task 7). Wave 1
/// ships only the always-pass 'stub' implementation (<c>StubTemplateLintService</c>,
/// WaAdmin.Infrastructure) so the pipeline shape — a lint run recorded per version — exists
/// ahead of Wave 3's real rules/LLM linters (#27). The <c>Linter</c> name identifies which
/// implementation produced the outcome (CHECK-enforced: stub | rules | llm).
/// </summary>
public interface ITemplateLintService
{
    /// <summary>Machine-readable linter identifier written to template_lint_results.linter.</summary>
    string Linter { get; }

    Task<TemplateLintOutcome> LintAsync(string componentsJson, CancellationToken cancellationToken);
}
