using Microsoft.Extensions.Diagnostics.HealthChecks;
using Neo4j.Driver;

namespace CodebaseIndexer.Host.Health;

/// <summary>Probes Neo4j bolt connectivity when GraphRAG is enabled.</summary>
public sealed class Neo4jHealthCheck : IHealthCheck
{
    private static readonly TimeSpan ProbeTimeout = TimeSpan.FromSeconds(5);

    private readonly IDriver _driver;

    /// <summary>Creates the health check.</summary>
    public Neo4jHealthCheck(IDriver driver) => _driver = driver;

    /// <inheritdoc />
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(ProbeTimeout);
        try
        {
            await _driver.VerifyConnectivityAsync().WaitAsync(cts.Token).ConfigureAwait(false);
            return HealthCheckResult.Healthy("Neo4j reachable");
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy("Neo4j unreachable", ex);
        }
    }
}
