using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using WaAdmin.Infrastructure.Messaging;
using Xunit;

namespace WaAdmin.Tests.Messaging;

/// <summary>Transport-layer guard tests, mirroring WaIngest.Tests' RabbitMqConnectionManagerTests
/// — the fail-closed-outside-Development posture is identical by design (issue #16).</summary>
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
}
