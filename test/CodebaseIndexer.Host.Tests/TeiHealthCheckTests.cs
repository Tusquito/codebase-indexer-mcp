using System.Net;
using CodebaseIndexer.Host.Health;
using CodebaseIndexer.Infrastructure.Tei;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace CodebaseIndexer.Host.Tests;

/// <summary>Unit tests for <see cref="TeiHealthCheck"/>.</summary>
public sealed class TeiHealthCheckTests
{
    /// <summary>Healthy when TEI /health returns success.</summary>
    [Test]
    public async Task CheckHealth_returns_healthy_on_success()
    {
        var api = ITeiEmbeddingsApi.Mock();
        api.GetHealthAsync(Any()).Returns(new HttpResponseMessage(HttpStatusCode.OK));

        var check = new TeiHealthCheck(api);
        var result = await check.CheckHealthAsync(new HealthCheckContext());
        await Assert.That(result.Status).IsEqualTo(HealthStatus.Healthy);
    }

    /// <summary>Unhealthy when TEI /health returns non-success.</summary>
    [Test]
    public async Task CheckHealth_returns_unhealthy_on_non_success()
    {
        var api = ITeiEmbeddingsApi.Mock();
        api.GetHealthAsync(Any()).Returns(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));

        var check = new TeiHealthCheck(api);
        var result = await check.CheckHealthAsync(new HealthCheckContext());
        await Assert.That(result.Status).IsEqualTo(HealthStatus.Unhealthy);
    }

    /// <summary>Unhealthy when TEI call throws.</summary>
    [Test]
    public async Task CheckHealth_returns_unhealthy_on_exception()
    {
        var api = ITeiEmbeddingsApi.Mock();
        api.GetHealthAsync(Any()).Throws(new HttpRequestException("connection refused"));

        var check = new TeiHealthCheck(api);
        var result = await check.CheckHealthAsync(new HealthCheckContext());
        await Assert.That(result.Status).IsEqualTo(HealthStatus.Unhealthy);
    }
}
