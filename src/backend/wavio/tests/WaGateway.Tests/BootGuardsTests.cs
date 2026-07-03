using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Moq;
using WaGateway.Infrastructure;
using Xunit;

namespace WaGateway.Tests;

public class BootGuardsTests
{
    private static IConfiguration EmptyConfig() => new ConfigurationBuilder().Build();

    private static IConfiguration ConfigWith(Dictionary<string, string?> values) =>
        new ConfigurationBuilder().AddInMemoryCollection(values).Build();

    private static IHostEnvironment FakeEnvironment(string name)
    {
        var env = new Mock<IHostEnvironment>();
        env.Setup(e => e.EnvironmentName).Returns(name);
        return env.Object;
    }

    [Fact]
    public void RequireRabbitMqConfiguredOutsideDevelopment_throws_when_missing_in_Production()
    {
        // Regression test (security review, PR #45, S2 — same lesson as issue #43's S1: an eager
        // boot-time check, not just a lazily-constructed component's own guard).
        var ex = Assert.Throws<InvalidOperationException>(() =>
            BootGuards.RequireRabbitMqConfiguredOutsideDevelopment(EmptyConfig(), FakeEnvironment("Production")));

        Assert.Contains("ConnectionStrings:RabbitMq", ex.Message);
    }

    [Fact]
    public void RequireRabbitMqConfiguredOutsideDevelopment_does_not_throw_in_Development()
    {
        var ex = Record.Exception(() =>
            BootGuards.RequireRabbitMqConfiguredOutsideDevelopment(EmptyConfig(), FakeEnvironment("Development")));

        Assert.Null(ex);
    }

    [Fact]
    public void RequireRabbitMqConfiguredOutsideDevelopment_does_not_throw_when_configured()
    {
        var config = ConfigWith(new() { ["ConnectionStrings:RabbitMq"] = "amqp://user:pass@rabbit.internal:5672" });

        var ex = Record.Exception(() =>
            BootGuards.RequireRabbitMqConfiguredOutsideDevelopment(config, FakeEnvironment("Production")));

        Assert.Null(ex);
    }

    [Fact]
    public void RequireMetaGraphConfiguredOutsideDevelopment_throws_when_missing_in_Production()
    {
        // Regression test (security review, PR #45, S2): without this, a message loops
        // dispatch->reclaim forever and never dead-letters — see BootGuards.cs's doc comment.
        var ex = Assert.Throws<InvalidOperationException>(() =>
            BootGuards.RequireMetaGraphConfiguredOutsideDevelopment(EmptyConfig(), FakeEnvironment("Production")));

        Assert.Contains("Meta:Graph:BaseUrl", ex.Message);
    }

    [Fact]
    public void RequireMetaGraphConfiguredOutsideDevelopment_throws_when_only_one_of_two_values_is_set()
    {
        var config = ConfigWith(new() { ["Meta:Graph:BaseUrl"] = "https://graph.facebook.com" });

        Assert.Throws<InvalidOperationException>(() =>
            BootGuards.RequireMetaGraphConfiguredOutsideDevelopment(config, FakeEnvironment("Production")));
    }

    [Fact]
    public void RequireMetaGraphConfiguredOutsideDevelopment_does_not_throw_in_Development()
    {
        var ex = Record.Exception(() =>
            BootGuards.RequireMetaGraphConfiguredOutsideDevelopment(EmptyConfig(), FakeEnvironment("Development")));

        Assert.Null(ex);
    }

    [Fact]
    public void RequireMetaGraphConfiguredOutsideDevelopment_does_not_throw_when_both_values_are_configured()
    {
        var config = ConfigWith(new()
        {
            ["Meta:Graph:BaseUrl"] = "https://graph.facebook.com",
            ["Meta:Graph:AccessToken"] = "real-token",
        });

        var ex = Record.Exception(() =>
            BootGuards.RequireMetaGraphConfiguredOutsideDevelopment(config, FakeEnvironment("Production")));

        Assert.Null(ex);
    }
}
