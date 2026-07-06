using WaAdmin.Application.Templates;
using WaAdmin.Infrastructure.Templates;
using Xunit;

namespace WaAdmin.Tests.Lint;

/// <summary>
/// Issue #27's own acceptance criterion for the vertical packs: "reviewed against category rules
/// so utility stays utility." Compiles each seeded pack's DSL through the exact same
/// TemplateDefinitionCompiler a real CreateTemplateCommand uses, then runs the exact same
/// RulesTemplateLintService the real submission pipeline uses — not a hand-inspection of the
/// authored text.
/// </summary>
public class VerticalTemplatePacksTests
{
    private static readonly RulesTemplateLintService Sut = new();

    public static IEnumerable<object[]> Packs() =>
        VerticalTemplatePacks.All.Select(p => new object[] { p.PackKey });

    [Theory]
    [MemberData(nameof(Packs))]
    public async Task SeededPack_LintsClean(string packKey)
    {
        var pack = VerticalTemplatePacks.All.Single(p => p.PackKey == packKey);
        var componentsJson = TemplateDefinitionCompiler.CompileComponents(pack.Definition);
        var category = TemplateDefinitionCompiler.CompileCategory(pack.Definition.Category);

        var outcome = await Sut.LintAsync(
            new WaAdmin.Application.Common.Interfaces.TemplateLintInput(category, pack.Definition.Language, componentsJson),
            CancellationToken.None);

        Assert.True(outcome.Passed, $"Pack '{packKey}' failed lint: {outcome.Findings}");
    }

    [Fact]
    public void All_HaveDistinctPackKeys()
    {
        var keys = VerticalTemplatePacks.All.Select(p => p.PackKey).ToList();
        Assert.Equal(keys.Distinct().Count(), keys.Count);
    }

    [Fact]
    public void All_CoverTheFiveRequiredVerticals()
    {
        var keys = VerticalTemplatePacks.All.Select(p => p.PackKey).ToHashSet();
        Assert.Equal(5, keys.Count);
        Assert.Contains("appointment_reminder", keys);
        Assert.Contains("pickup_scheduled", keys);
        Assert.Contains("order_ready", keys);
        Assert.Contains("payment_link", keys);
        Assert.Contains("otp", keys);
    }
}
