using CodebaseIndexer.Host.Health;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Neo4j.Driver;

namespace CodebaseIndexer.Host.Tests;

/// <summary>Unit tests for <see cref="Neo4jHealthCheck"/>.</summary>
public sealed class Neo4jHealthCheckTests
{
    /// <summary>Healthy when VerifyConnectivityAsync succeeds.</summary>
    [Fact]
    public async Task CheckHealth_returns_healthy_when_connected()
    {
        var check = new Neo4jHealthCheck(new StubNeo4jDriver(healthy: true));
        var result = await check.CheckHealthAsync(new HealthCheckContext());
        Assert.Equal(HealthStatus.Healthy, result.Status);
    }

    /// <summary>Unhealthy when VerifyConnectivityAsync throws.</summary>
    [Fact]
    public async Task CheckHealth_returns_unhealthy_when_unreachable()
    {
        var check = new Neo4jHealthCheck(new StubNeo4jDriver(healthy: false));
        var result = await check.CheckHealthAsync(new HealthCheckContext());
        Assert.Equal(HealthStatus.Unhealthy, result.Status);
    }
}
