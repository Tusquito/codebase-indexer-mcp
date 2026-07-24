using CodebaseIndexer.Host.Health;
using CodebaseIndexer.Infrastructure.Colbert;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace CodebaseIndexer.Host.Tests;

/// <summary>Unit tests for <see cref="ColbertRemoteHealthCheck"/>.</summary>
public sealed class ColbertRemoteHealthCheckTests
{
    /// <summary>Healthy when sidecar /health succeeds.</summary>
    [Test]
    public async Task CheckHealth_returns_healthy_on_success()
    {
        var api = IColbertEmbedApi.Mock();
        api.GetHealthAsync(Any()).Returns(new ColbertHealthResponse(
            Model: "colbert-ir/colbertv2.0",
            TokenDimension: 128,
            Loaded: true,
            Device: "cpu",
            ExecutionProviders: ["CPUExecutionProvider"],
            CudaAvailable: false));

        var check = new ColbertRemoteHealthCheck(api);
        var result = await check.CheckHealthAsync(new HealthCheckContext());
        await Assert.That(result.Status).IsEqualTo(HealthStatus.Healthy);
    }

    /// <summary>Unhealthy when sidecar /health throws.</summary>
    [Test]
    public async Task CheckHealth_returns_unhealthy_on_exception()
    {
        var api = IColbertEmbedApi.Mock();
        api.GetHealthAsync(Any()).Throws(new HttpRequestException("ColBERT sidecar unreachable"));

        var check = new ColbertRemoteHealthCheck(api);
        var result = await check.CheckHealthAsync(new HealthCheckContext());
        await Assert.That(result.Status).IsEqualTo(HealthStatus.Unhealthy);
    }
}
