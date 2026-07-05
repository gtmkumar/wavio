using WaAdmin.Application.Consent.Logic;
using Xunit;

namespace WaAdmin.Tests.Consent;

public class OptOutKeywordMatcherTests
{
    [Theory]
    [InlineData("STOP")]
    [InlineData("stop")]
    [InlineData("please STOP now")]
    [InlineData("  stop  ")]
    public void TryMatch_EnglishStopVariants_MatchesEnglish(string text)
    {
        var result = OptOutKeywordMatcher.TryMatch(text);

        Assert.NotNull(result);
        Assert.Equal("stop", result!.Value.Keyword);
        Assert.Equal("en", result.Value.Language);
    }

    [Fact]
    public void TryMatch_Unsubscribe_MatchesEnglish()
    {
        var result = OptOutKeywordMatcher.TryMatch("Please unsubscribe me from these messages");

        Assert.NotNull(result);
        Assert.Equal("unsubscribe", result!.Value.Keyword);
    }

    [Fact]
    public void TryMatch_DevanagariBand_MatchesHindi()
    {
        var result = OptOutKeywordMatcher.TryMatch("बंद");

        Assert.NotNull(result);
        Assert.Equal("बंद", result!.Value.Keyword);
        Assert.Equal("hi", result.Value.Language);
    }

    [Fact]
    public void TryMatch_DevanagariRoko_MatchesHindi()
    {
        var result = OptOutKeywordMatcher.TryMatch("कृपया रोको");

        Assert.NotNull(result);
        Assert.Equal("रोको", result!.Value.Keyword);
        Assert.Equal("hi", result.Value.Language);
    }

    [Theory]
    [InlineData("band karo")]
    [InlineData("Band Karo!")]
    [InlineData("please band karo now")]
    public void TryMatch_RomanizedHindiBandKaro_MatchesRomanizedHindi(string text)
    {
        var result = OptOutKeywordMatcher.TryMatch(text);

        Assert.NotNull(result);
        Assert.Equal("band karo", result!.Value.Keyword);
        Assert.Equal("hi-Latn", result.Value.Language);
    }

    [Fact]
    public void TryMatch_TextContainingKeywordAsSubstringOfAnotherWord_DoesNotMatch()
    {
        // "stopwatch" must not trigger on "stop" — token-based matching, not substring.
        var result = OptOutKeywordMatcher.TryMatch("my stopwatch is broken");

        Assert.Null(result);
    }

    [Fact]
    public void TryMatch_OrdinaryConversationalText_DoesNotMatch()
    {
        var result = OptOutKeywordMatcher.TryMatch("What time does the store open tomorrow?");

        Assert.Null(result);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void TryMatch_NullOrWhitespaceText_DoesNotMatch(string? text)
    {
        var result = OptOutKeywordMatcher.TryMatch(text);

        Assert.Null(result);
    }

    [Fact]
    public void TryMatch_BuiltInVocabularyWinsOverExtraVocabularyOnConflict()
    {
        // "stop" appears in both the default vocabulary (en) and a hypothetical per-vertical
        // extension with a different language tag — the default vocabulary is checked first and
        // must win, so a vertical addition can only ADD detections, never shadow a platform default.
        var extra = new List<(string Phrase, string Language)> { ("stop", "vertical-override") };

        var result = OptOutKeywordMatcher.TryMatch("stop", extra);

        Assert.NotNull(result);
        Assert.Equal("en", result!.Value.Language);
    }

    [Fact]
    public void TryMatch_PerVerticalExtraVocabulary_IsDetectedWhenNotInDefaultVocabulary()
    {
        var extra = new List<(string Phrase, string Language)> { ("band kar do", "hi-Latn") };

        var result = OptOutKeywordMatcher.TryMatch("please band kar do abhi", extra);

        Assert.NotNull(result);
        Assert.Equal("band kar do", result!.Value.Keyword);
    }

    [Fact]
    public void IsOptOutKeyword_ReturnsBooleanConvenienceForm()
    {
        Assert.True(OptOutKeywordMatcher.IsOptOutKeyword("STOP"));
        Assert.False(OptOutKeywordMatcher.IsOptOutKeyword("hello there"));
    }
}
