using CodebaseIndexer.Host.Health;
using CodebaseIndexer.Infrastructure.Colbert;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace CodebaseIndexer.Host.Tests;

/// <summary>Unit tests for <see cref="ColbertRemoteHealthCheck"/>.</summary>
public sealed class ColbertRemoteHealthCheckTests
{
    /// <summary>Healthy when sidecar /health succeeds.</summary>
    [Fact]
    public async Task CheckHealth_returns_healthy_on_success()
    {
        var check = new ColbertRemoteHealthCheck(new StubColbertEmbedApi(healthy: true));
        var result = await check.CheckHealthAsync(new HealthCheckContext());
        Assert.Equal(HealthStatus.Healthy, result.Status);
    }

    /// <summary>Unhealthy when sidecar /health throws.</summary>
    [Fact]
    public async Task CheckHealth_returns_unhealthy_on_exception()
    {
        var check = new ColbertRemoteHealthCheck(new StubColbertEmbedApi(healthy: false));
        var result = await check.CheckHealthAsync(new HealthCheckContext());
        Assert.Equal(HealthStatus.Unhealthy, result.Status);
    }
}
