using CodebaseIndexer.Infrastructure.Tei;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace CodebaseIndexer.Host.Health;

/// <summary>Probes TEI dense embedding readiness (required dependency).</summary>
public sealed class TeiHealthCheck : IHealthCheck
{
    private static readonly TimeSpan ProbeTimeout = TimeSpan.FromSeconds(5);

    private readonly ITeiEmbeddingsApi _api;

    /// <summary>Creates the health check.</summary>
    public TeiHealthCheck(ITeiEmbeddingsApi api) => _api = api;

    /// <inheritdoc />
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(ProbeTimeout);
        try
        {
            using var response = await _api.GetHealthAsync(cts.Token).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                return HealthCheckResult.Unhealthy(
                    $"TEI health returned {(int)response.StatusCode}");
            }

            return HealthCheckResult.Healthy("TEI reachable");
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy("TEI unreachable", ex);
        }
    }
}
