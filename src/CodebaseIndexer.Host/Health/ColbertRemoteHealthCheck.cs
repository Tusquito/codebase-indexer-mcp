using CodebaseIndexer.Infrastructure.Colbert;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace CodebaseIndexer.Host.Health;

/// <summary>Probes the remote ColBERT sidecar when rerank uses the remote backend.</summary>
public sealed class ColbertRemoteHealthCheck : IHealthCheck
{
    private readonly IColbertEmbedApi _api;

    /// <summary>Creates the health check.</summary>
    public ColbertRemoteHealthCheck(IColbertEmbedApi api) => _api = api;

    /// <inheritdoc />
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var health = await _api.GetHealthAsync(cancellationToken).ConfigureAwait(false);
            var device = health.Device ?? "unknown";
            return HealthCheckResult.Healthy($"colbert device={device} loaded={health.Loaded}");
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy("ColBERT sidecar unreachable", ex);
        }
    }
}
