using WaAdmin.Application.Common.Interfaces;

namespace WaAdmin.Infrastructure.Templates;

/// <summary>
/// Wave 1 lint implementation (issue #16 Task 7): always passes, records an empty findings array.
/// Superseded in the DI pipeline by <see cref="RulesTemplateLintService"/> (+ optionally
/// <see cref="LlmTemplateLintService"/>) as of issue #27 — kept only as a lightweight fixture for
/// tests that don't care about real lint behavior, not wired into WaAdmin.Infrastructure's
/// DependencyInjection anymore.
/// </summary>
public sealed class StubTemplateLintService : ITemplateLintService
{
    public string Linter => "stub";

    public Task<TemplateLintOutcome> LintAsync(TemplateLintInput input, CancellationToken cancellationToken) =>
        Task.FromResult(new TemplateLintOutcome(Passed: true, Findings: "[]", Score: null));
}
