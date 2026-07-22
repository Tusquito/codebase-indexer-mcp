using CodebaseIndexer.Host.Health;
using CodebaseIndexer.Infrastructure.Tei;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace CodebaseIndexer.Host.Tests;

/// <summary>Unit tests for <see cref="TeiHealthCheck"/>.</summary>
public sealed class TeiHealthCheckTests
{
    /// <summary>Healthy when TEI /health returns success.</summary>
    [Fact]
    public async Task CheckHealth_returns_healthy_on_success()
    {
        var check = new TeiHealthCheck(new StubTeiEmbeddingsApi(healthy: true));
        var result = await check.CheckHealthAsync(new HealthCheckContext());
        Assert.Equal(HealthStatus.Healthy, result.Status);
    }

    /// <summary>Unhealthy when TEI /health returns non-success.</summary>
    [Fact]
    public async Task CheckHealth_returns_unhealthy_on_non_success()
    {
        var check = new TeiHealthCheck(new StubTeiEmbeddingsApi(healthy: false));
        var result = await check.CheckHealthAsync(new HealthCheckContext());
        Assert.Equal(HealthStatus.Unhealthy, result.Status);
    }

    /// <summary>Unhealthy when TEI call throws.</summary>
    [Fact]
    public async Task CheckHealth_returns_unhealthy_on_exception()
    {
        var check = new TeiHealthCheck(new ThrowingTeiEmbeddingsApi());
        var result = await check.CheckHealthAsync(new HealthCheckContext());
        Assert.Equal(HealthStatus.Unhealthy, result.Status);
    }

    private sealed class ThrowingTeiEmbeddingsApi : ITeiEmbeddingsApi
    {
        public Task<HttpResponseMessage> GetHealthAsync(CancellationToken cancellationToken = default) =>
            throw new HttpRequestException("connection refused");

        public Task<EmbeddingsResponse> CreateEmbeddingsAsync(
            EmbeddingsRequest request,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }
}
