using WaGateway.Infrastructure.Graph;
using Xunit;

namespace WaGateway.Tests.Graph;

public class GraphErrorClassifierTests
{
    [Fact]
    public void Http429_is_transient()
    {
        Assert.True(GraphErrorClassifier.IsTransient(429, metaErrorCode: null));
    }

    [Theory]
    [InlineData(500)]
    [InlineData(502)]
    [InlineData(503)]
    public void Http5xx_is_transient(int statusCode)
    {
        Assert.True(GraphErrorClassifier.IsTransient(statusCode, metaErrorCode: null));
    }

    [Theory]
    [InlineData(131026)]
    [InlineData(131047)]
    [InlineData(131049)]
    public void Recognized_permanent_Meta_error_codes_are_never_transient_regardless_of_http_status(int errorCode)
    {
        Assert.False(GraphErrorClassifier.IsTransient(400, errorCode));
        // Even if Meta wraps a permanent code in a 5xx — the reason it will never succeed hasn't
        // changed just because Graph is also having an infra-side bad day.
        Assert.False(GraphErrorClassifier.IsTransient(500, errorCode));
    }

    [Fact]
    public void An_unrecognized_4xx_error_is_treated_as_permanent_by_default()
    {
        Assert.False(GraphErrorClassifier.IsTransient(400, metaErrorCode: 999999));
    }

    [Fact]
    public void Http400_with_no_error_code_at_all_is_permanent()
    {
        Assert.False(GraphErrorClassifier.IsTransient(400, metaErrorCode: null));
    }
}
