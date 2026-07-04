namespace wavio.SharedDataModel.Entities.Templates;

/// <summary>
/// Per-version lint run (templates.template_lint_results, issue #16). Wave 1 ships only the
/// 'stub' linter (always <see cref="Passed"/> = true, empty <see cref="Findings"/>) so the
/// pipeline shape exists; Wave 3 (#27) adds 'rules' and 'llm' linters that populate real findings.
/// </summary>
public class TemplateLintResult
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid TemplateVersionId { get; set; }

    /// <summary>stub | rules | llm (CHECK-enforced).</summary>
    public string Linter { get; set; } = "stub";

    public bool Passed { get; set; }

    /// <summary>JSON array of finding objects (jsonb) — empty array for the always-pass stub.</summary>
    public string Findings { get; set; } = "[]";

    public short? Score { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
    public Guid? CreatedBy { get; set; }
}
