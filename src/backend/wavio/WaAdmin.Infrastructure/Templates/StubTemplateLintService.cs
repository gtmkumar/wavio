using WaAdmin.Application.Common.Interfaces;

namespace WaAdmin.Infrastructure.Templates;

/// <summary>
/// Wave 1 lint implementation (issue #16 Task 7): always passes, records an empty findings array.
/// Exists purely so the pipeline shape — every version gets a lint run recorded in
/// templates.template_lint_results before submission — is real ahead of Wave 3's actual
/// rules/LLM linters (#27), which will add their own <see cref="ITemplateLintService"/>
/// implementations (Linter = "rules" / "llm") without touching any caller of this interface.
/// </summary>
public sealed class StubTemplateLintService : ITemplateLintService
{
    public string Linter => "stub";

    public Task<TemplateLintOutcome> LintAsync(string componentsJson, CancellationToken cancellationToken) =>
        Task.FromResult(new TemplateLintOutcome(Passed: true, Findings: "[]", Score: null));
}
