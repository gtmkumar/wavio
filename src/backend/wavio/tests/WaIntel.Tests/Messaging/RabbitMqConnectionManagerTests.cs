using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using WaIntel.Infrastructure.Messaging;
using Xunit;

namespace WaIntel.Tests.Messaging;

public class RabbitMqConnectionManagerTests
{
    private static IConfiguration EmptyConfig() => new ConfigurationBuilder().Build();

    private static IHostEnvironment FakeEnvironment(string name)
    {
        var env = new Mock<IHostEnvironment>();
        env.Setup(e => e.EnvironmentName).Returns(name);
        return env.Object;
    }

    [Fact]
    public void Constructor_MissingConnectionStringOutsideDevelopment_ThrowsAtConstruction()
    {
        // Regression test (issue #15 security review, S1): this branch was cut before WaIngest's
        // equivalent fail-closed fix (issue #13 security review, S2) landed, so it silently fell
        // back to amqp://guest:guest@localhost outside Development too. Must fail CLOSED.
        var ex = Assert.Throws<InvalidOperationException>(() =>
            new RabbitMqConnectionManager(EmptyConfig(), FakeEnvironment("Production"), NullLogger<RabbitMqConnectionManager>.Instance));

        Assert.Contains("ConnectionStrings:RabbitMq", ex.Message);
    }

    [Fact]
    public void Constructor_MissingConnectionStringInDevelopment_FallsBackWithoutThrowing()
    {
        var ex = Record.Exception(() =>
            new RabbitMqConnectionManager(EmptyConfig(), FakeEnvironment("Development"), NullLogger<RabbitMqConnectionManager>.Instance));

        Assert.Null(ex);
    }

    [Fact]
    public void Constructor_ConfiguredConnectionString_NeverThrowsRegardlessOfEnvironment()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:RabbitMq"] = "amqp://user:pass@rabbit.internal:5672"
            })
            .Build();

        var ex = Record.Exception(() =>
            new RabbitMqConnectionManager(config, FakeEnvironment("Production"), NullLogger<RabbitMqConnectionManager>.Instance));

        Assert.Null(ex);
    }
}
