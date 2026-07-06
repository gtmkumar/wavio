namespace WaAdmin.Application.Common.Interfaces;

/// <summary>
/// Everything a linter needs to evaluate policy compliance for a template version, without
/// parsing the template row out of band (issue #27). <see cref="Category"/> in particular drives
/// category-specific rules (promotional language is only a violation in utility templates; a
/// missing opt-out instruction only applies to marketing).
/// </summary>
/// <param name="Category">marketing | utility | authentication — matches templates.templates.category.</param>
/// <param name="Language">BCP-47 / Meta language code (e.g. en, en_US, hi).</param>
/// <param name="ComponentsJson">Compiled Meta component JSON array (TemplateDefinitionCompiler.CompileComponents output).</param>
public sealed record TemplateLintInput(string Category, string Language, string ComponentsJson);

/// <summary>Outcome of a lint run, persisted verbatim to templates.template_lint_results.</summary>
/// <param name="Passed">Whether the version may proceed to submission.</param>
/// <param name="Findings">JSON array of finding objects (jsonb) — empty for a clean pass.</param>
/// <param name="Score">Optional 0-100 quality score; null when the linter doesn't score.</param>
public sealed record TemplateLintOutcome(bool Passed, string Findings, short? Score);

/// <summary>
/// Lints a template version's content before submission (spec §4.4). Wave 1 shipped only the
/// always-pass 'stub' implementation (<c>StubTemplateLintService</c>, WaAdmin.Infrastructure,
/// issue #16) so the pipeline shape — a lint run recorded per version — existed ahead of Wave 3's
/// real rules/LLM linters (<c>RulesTemplateLintService</c> / <c>LlmTemplateLintService</c>, issue
/// #27). Multiple implementations may be registered simultaneously (resolved as
/// <c>IEnumerable&lt;ITemplateLintService&gt;</c> by <see cref="ITemplateSubmissionService"/>,
/// which runs every registered linter and records one template_lint_results row per linter) — the
/// <c>Linter</c> name identifies which implementation produced a given outcome (CHECK-enforced:
/// stub | rules | llm).
/// </summary>
public interface ITemplateLintService
{
    /// <summary>Machine-readable linter identifier written to template_lint_results.linter.</summary>
    string Linter { get; }

    Task<TemplateLintOutcome> LintAsync(TemplateLintInput input, CancellationToken cancellationToken);
}
