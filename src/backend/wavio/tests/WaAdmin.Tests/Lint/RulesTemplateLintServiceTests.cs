using System.Text.Json.Nodes;
using WaAdmin.Application.Common.Interfaces;
using WaAdmin.Infrastructure.Templates;
using Xunit;

namespace WaAdmin.Tests.Lint;

/// <summary>
/// Table-driven coverage of RulesTemplateLintService (issue #27). The known-bad fixtures here are
/// the issue's own acceptance criterion: "Known-bad fixtures (promo-in-utility etc.) blocked
/// pre-submission" — each MUST come back Passed = false; the clean fixtures MUST come back
/// Passed = true so the linter isn't just rejecting everything.
/// </summary>
public class RulesTemplateLintServiceTests
{
    private static readonly RulesTemplateLintService Sut = new();

    private static string Body(string text) =>
        new JsonArray { new JsonObject { ["type"] = "BODY", ["text"] = text } }.ToJsonString();

    [Fact]
    public async Task LintAsync_UtilityWithPromotionalLanguage_Blocks()
    {
        var input = new TemplateLintInput("utility", "en_US",
            Body("Hi {{1}}, enjoy a huge discount on your next order!"));

        var outcome = await Sut.LintAsync(input, CancellationToken.None);

        Assert.False(outcome.Passed);
        Assert.Contains("UTILITY_PROMOTIONAL_LANGUAGE", outcome.Findings);
    }

    [Fact]
    public async Task LintAsync_MarketingWithoutOptOut_Blocks()
    {
        var input = new TemplateLintInput("marketing", "en_US",
            Body("Hi {{1}}, check out our new spring collection today."));

        var outcome = await Sut.LintAsync(input, CancellationToken.None);

        Assert.False(outcome.Passed);
        Assert.Contains("MARKETING_MISSING_OPT_OUT", outcome.Findings);
    }

    [Fact]
    public async Task LintAsync_MarketingWithOptOut_Passes()
    {
        var input = new TemplateLintInput("marketing", "en_US",
            Body("Hi {{1}}, check out our new spring collection today. Reply STOP to opt out."));

        var outcome = await Sut.LintAsync(input, CancellationToken.None);

        Assert.True(outcome.Passed);
    }

    [Fact]
    public async Task LintAsync_VariableLeadingText_Blocks()
    {
        var input = new TemplateLintInput("utility", "en_US", Body("{{1}} is ready for pickup."));

        var outcome = await Sut.LintAsync(input, CancellationToken.None);

        Assert.False(outcome.Passed);
        Assert.Contains("VARIABLE_LEADING_OR_TRAILING", outcome.Findings);
    }

    [Fact]
    public async Task LintAsync_TooManyVariablesRelativeToText_Blocks()
    {
        var input = new TemplateLintInput("utility", "en_US",
            Body("Order {{1}} {{2}} {{3}} {{4}} {{5}} ready {{6}}."));

        var outcome = await Sut.LintAsync(input, CancellationToken.None);

        Assert.False(outcome.Passed);
        Assert.Contains("VARIABLE_DENSITY_TOO_HIGH", outcome.Findings);
    }

    [Fact]
    public async Task LintAsync_MalformedPlaceholderSingleBrace_Blocks()
    {
        var input = new TemplateLintInput("utility", "en_US", Body("Your order {1} is ready."));

        var outcome = await Sut.LintAsync(input, CancellationToken.None);

        Assert.False(outcome.Passed);
        Assert.Contains("FORMATTING_MALFORMED_PLACEHOLDER", outcome.Findings);
    }

    [Fact]
    public async Task LintAsync_MalformedPlaceholderNonNumeric_Blocks()
    {
        var input = new TemplateLintInput("utility", "en_US", Body("Your order {{name}} is ready."));

        var outcome = await Sut.LintAsync(input, CancellationToken.None);

        Assert.False(outcome.Passed);
        Assert.Contains("FORMATTING_MALFORMED_PLACEHOLDER", outcome.Findings);
    }

    [Fact]
    public async Task LintAsync_EmptyBody_Blocks()
    {
        var input = new TemplateLintInput("utility", "en_US", "[]");

        var outcome = await Sut.LintAsync(input, CancellationToken.None);

        Assert.False(outcome.Passed);
        Assert.Contains("FORMATTING_EMPTY_BODY", outcome.Findings);
    }

    [Fact]
    public async Task LintAsync_AllCapsShouting_Blocks()
    {
        var input = new TemplateLintInput("utility", "en_US",
            Body("YOUR ORDER NUMBER {{1}} HAS SHIPPED TODAY"));

        var outcome = await Sut.LintAsync(input, CancellationToken.None);

        Assert.False(outcome.Passed);
        Assert.Contains("FORMATTING_ALL_CAPS", outcome.Findings);
    }

    [Fact]
    public async Task LintAsync_ExcessivePunctuation_Blocks()
    {
        var input = new TemplateLintInput("utility", "en_US",
            Body("Your order {{1}} has shipped!!! Track it now!!!"));

        var outcome = await Sut.LintAsync(input, CancellationToken.None);

        Assert.False(outcome.Passed);
        Assert.Contains("FORMATTING_EXCESSIVE_PUNCTUATION_OR_EMOJI", outcome.Findings);
    }

    [Fact]
    public async Task LintAsync_ExcessiveEmoji_Blocks()
    {
        var input = new TemplateLintInput("utility", "en_US",
            Body("Your order {{1}} has shipped 🚀🎉🔥💯"));

        var outcome = await Sut.LintAsync(input, CancellationToken.None);

        Assert.False(outcome.Passed);
        Assert.Contains("FORMATTING_EXCESSIVE_PUNCTUATION_OR_EMOJI", outcome.Findings);
    }

    [Fact]
    public async Task LintAsync_CleanUtilityTemplate_Passes()
    {
        var input = new TemplateLintInput("utility", "en_US",
            Body("Hi {{1}}, your order {{2}} has shipped and will arrive by {{3}}."));

        var outcome = await Sut.LintAsync(input, CancellationToken.None);

        Assert.True(outcome.Passed);
        Assert.Equal("[]", outcome.Findings);
        Assert.Equal((short?)100, outcome.Score);
    }

    [Fact]
    public async Task LintAsync_CleanAuthenticationTemplate_Passes()
    {
        var input = new TemplateLintInput("authentication", "en_US",
            Body("Your verification code is {{1}}. Do not share this code with anyone."));

        var outcome = await Sut.LintAsync(input, CancellationToken.None);

        Assert.True(outcome.Passed);
    }
}
