using System.Globalization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using WaGateway.Infrastructure;
using Xunit;

namespace WaGateway.Tests;

public class DependencyInjectionTests
{
    private static IConfiguration BuildConfig(int? staleLockSeconds, int? graphTimeoutSeconds)
    {
        var values = new Dictionary<string, string?>();
        if (staleLockSeconds.HasValue) values["Outbox:StaleLockSeconds"] = staleLockSeconds.Value.ToString(CultureInfo.InvariantCulture);
        if (graphTimeoutSeconds.HasValue) values["Meta:Graph:TimeoutSeconds"] = graphTimeoutSeconds.Value.ToString(CultureInfo.InvariantCulture);
        return new ConfigurationBuilder().AddInMemoryCollection(values).Build();
    }

    [Fact]
    public void AddWaGatewayInfrastructure_throws_when_configured_timeout_is_not_strictly_less_than_stale_lock_window()
    {
        // Regression test (security review, PR #45, S1): a Graph timeout >= the stale-lock
        // reclaim window lets a slow-but-alive call still be in flight when its lease is
        // reclaimed — this must fail fast at boot, not silently misbehave in production.
        var services = new ServiceCollection();
        var config = BuildConfig(staleLockSeconds: 30, graphTimeoutSeconds: 30);

        var ex = Assert.Throws<InvalidOperationException>(() => services.AddWaGatewayInfrastructure(config));

        Assert.Contains("Meta:Graph:TimeoutSeconds", ex.Message);
        Assert.Contains("Outbox:StaleLockSeconds", ex.Message);
    }

    [Fact]
    public void AddWaGatewayInfrastructure_throws_when_configured_timeout_exceeds_the_stale_lock_window()
    {
        var services = new ServiceCollection();
        var config = BuildConfig(staleLockSeconds: 20, graphTimeoutSeconds: 25);

        Assert.Throws<InvalidOperationException>(() => services.AddWaGatewayInfrastructure(config));
    }

    [Fact]
    public void AddWaGatewayInfrastructure_does_not_throw_with_explicit_headroom()
    {
        var services = new ServiceCollection();
        var config = BuildConfig(staleLockSeconds: 30, graphTimeoutSeconds: 20);

        var ex = Record.Exception(() => services.AddWaGatewayInfrastructure(config));

        Assert.Null(ex);
    }

    [Fact]
    public void AddWaGatewayInfrastructure_computes_a_safe_default_timeout_when_unconfigured()
    {
        // No Meta:Graph:TimeoutSeconds at all — the computed default (staleLockSeconds - 5,
        // floor 5) must always satisfy the invariant on its own.
        var services = new ServiceCollection();
        var config = BuildConfig(staleLockSeconds: 30, graphTimeoutSeconds: null);

        var ex = Record.Exception(() => services.AddWaGatewayInfrastructure(config));

        Assert.Null(ex);
    }

    [Fact]
    public void AddWaGatewayInfrastructure_does_not_throw_with_defaults_when_nothing_is_configured()
    {
        var services = new ServiceCollection();
        var config = BuildConfig(staleLockSeconds: null, graphTimeoutSeconds: null);

        var ex = Record.Exception(() => services.AddWaGatewayInfrastructure(config));

        Assert.Null(ex);
    }
}
