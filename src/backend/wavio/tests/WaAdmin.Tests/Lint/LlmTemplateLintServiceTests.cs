using System.Net;
using WaAdmin.Application.Common.Interfaces;
using WaAdmin.Infrastructure.Templates;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace WaAdmin.Tests.Lint;

/// <summary>
/// LlmTemplateLintService's documented degrade-on-failure contract (issue #27): a disabled
/// config, a transport failure, and an unparseable response all come back as a non-blocking
/// "skipped" outcome (Passed = true, one info-severity finding) — the LLM pass must never crash
/// or block submission just because Anthropic is unreachable or misconfigured. Only a genuine
/// model verdict can produce a blocking finding.
/// </summary>
public class LlmTemplateLintServiceTests
{
    private static readonly TemplateLintInput Input = new("utility", "en_US", """[{"type":"BODY","text":"Hi {{1}}."}]""");

    private static LlmTemplateLintService Sut(FakeHttpMessageHandler handler, bool enabled = true, string apiKey = "test-key") =>
        new(new HttpClient(handler) { BaseAddress = new Uri("https://api.anthropic.test") },
            Options.Create(new LintLlmOptions { Enabled = enabled, ApiKey = apiKey, Model = "claude-opus-4-8" }),
            NullLogger<LlmTemplateLintService>.Instance);

    [Fact]
    public async Task LintAsync_Disabled_SkipsWithoutCallingHttp()
    {
        var handler = new FakeHttpMessageHandler(_ => throw new InvalidOperationException("should not be called"));
        var sut = Sut(handler, enabled: false);

        var outcome = await sut.LintAsync(Input, CancellationToken.None);

        Assert.True(outcome.Passed);
        Assert.Contains("LLM_SKIPPED", outcome.Findings);
    }

    [Fact]
    public async Task LintAsync_TransportFailure_DoesNotThrow_ReturnsNonBlockingSkip()
    {
        var handler = new FakeHttpMessageHandler(_ => throw new HttpRequestException("connection refused"));
        var sut = Sut(handler);

        var outcome = await sut.LintAsync(Input, CancellationToken.None);

        Assert.True(outcome.Passed);
        Assert.Contains("LLM_SKIPPED", outcome.Findings);
    }

    [Fact]
    public async Task LintAsync_NonSuccessStatus_ReturnsNonBlockingSkip()
    {
        var handler = new FakeHttpMessageHandler(_ =>
            new HttpResponseMessage(HttpStatusCode.InternalServerError) { Content = new StringContent("boom") });
        var sut = Sut(handler);

        var outcome = await sut.LintAsync(Input, CancellationToken.None);

        Assert.True(outcome.Passed);
        Assert.Contains("LLM_SKIPPED", outcome.Findings);
    }

    [Fact]
    public async Task LintAsync_UnparseableBody_ReturnsNonBlockingSkip()
    {
        var handler = new FakeHttpMessageHandler(_ =>
            new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("not json") });
        var sut = Sut(handler);

        var outcome = await sut.LintAsync(Input, CancellationToken.None);

        Assert.True(outcome.Passed);
        Assert.Contains("LLM_SKIPPED", outcome.Findings);
    }

    [Fact]
    public async Task LintAsync_ValidJsonWrongShapeEnvelope_ReturnsNonBlockingSkip()
    {
        // Valid JSON, wrong shape (e.g. a proxy answering 200 with its own body): "content" is a
        // string, not the Messages API content-block array — AsArray()/GetValue<T>() throw
        // InvalidOperationException, which must degrade to "skipped" exactly like malformed JSON
        // (issue #27 security review, finding 3).
        const string body = """{"content":"maintenance"}""";
        var handler = new FakeHttpMessageHandler(_ =>
            new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body) });
        var sut = Sut(handler);

        var outcome = await sut.LintAsync(Input, CancellationToken.None);

        Assert.True(outcome.Passed);
        Assert.Contains("LLM_SKIPPED", outcome.Findings);
    }

    [Fact]
    public async Task LintAsync_Refusal_ReturnsNonBlockingSkip()
    {
        const string body = """{"content":[],"stop_reason":"refusal"}""";
        var handler = new FakeHttpMessageHandler(_ =>
            new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body) });
        var sut = Sut(handler);

        var outcome = await sut.LintAsync(Input, CancellationToken.None);

        Assert.True(outcome.Passed);
        Assert.Contains("LLM_SKIPPED", outcome.Findings);
    }

    [Fact]
    public async Task LintAsync_SuccessfulVerdictFailing_ReturnsBlockingOutcome()
    {
        const string verdict = """
            {"passed":false,"score":40,"findings":[{"code":"UTILITY_PROMOTIONAL_LANGUAGE","severity":"error","message":"reads as promotional","suggestedFix":"remove urgency language"}]}
            """;
        var body = $$"""{"content":[{"type":"text","text":{{System.Text.Json.JsonSerializer.Serialize(verdict)}}}],"stop_reason":"end_turn"}""";
        var handler = new FakeHttpMessageHandler(_ =>
            new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body) });
        var sut = Sut(handler);

        var outcome = await sut.LintAsync(Input, CancellationToken.None);

        Assert.False(outcome.Passed);
        Assert.Equal((short?)40, outcome.Score);
        Assert.Contains("UTILITY_PROMOTIONAL_LANGUAGE", outcome.Findings);
    }

    [Fact]
    public async Task LintAsync_SuccessfulVerdictPassing_ReturnsPassingOutcome()
    {
        const string verdict = """{"passed":true,"score":95,"findings":[]}""";
        var body = $$"""{"content":[{"type":"text","text":{{System.Text.Json.JsonSerializer.Serialize(verdict)}}}],"stop_reason":"end_turn"}""";
        var handler = new FakeHttpMessageHandler(_ =>
            new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body) });
        var sut = Sut(handler);

        var outcome = await sut.LintAsync(Input, CancellationToken.None);

        Assert.True(outcome.Passed);
        Assert.Equal((short?)95, outcome.Score);
    }

    // ── LintLlmOptions.IsValid (the ValidateOnStart predicate; issue #27 security review,
    // finding 1): enabled-but-misconfigured must fail the host at boot, never silently skip. ──

    [Theory]
    [InlineData(false, "", "https://api.anthropic.com", true)]   // disabled: always valid
    [InlineData(true, "key", "https://api.anthropic.com", true)] // enabled + key + https: valid
    [InlineData(true, "", "https://api.anthropic.com", false)]   // enabled, no key
    [InlineData(true, "key", "http://api.anthropic.com", false)] // enabled, non-https BaseUrl
    [InlineData(true, "key", "not a url", false)]                // enabled, invalid BaseUrl
    public void IsValid_EnabledRequiresApiKeyAndHttpsBaseUrl(bool enabled, string apiKey, string baseUrl, bool expected)
    {
        var options = new LintLlmOptions { Enabled = enabled, ApiKey = apiKey, BaseUrl = baseUrl };

        Assert.Equal(expected, LintLlmOptions.IsValid(options));
    }

    /// <summary>Minimal fake transport — no mocking framework needed for a single-call HttpClient.</summary>
    private sealed class FakeHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(respond(request));
    }
}
