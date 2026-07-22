using CodebaseIndexer.Application.Services;
using CodebaseIndexer.Domain.Models;
using CodebaseIndexer.Domain.Serialization;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using AspNetHealthStatus = Microsoft.Extensions.Diagnostics.HealthChecks.HealthStatus;
using HostHealthStatus = CodebaseIndexer.Application.Services.HealthStatus;

namespace CodebaseIndexer.Host.Health;

/// <summary>
/// Process liveness check backed by <see cref="IHealthService"/> (no dependency probes).
/// Tagged <c>live</c> for <c>GET /alive</c>; readiness deps live on separate checks.
/// </summary>
public sealed class McpHostHealthCheck(IHealthService health) : IHealthCheck
{
    /// <inheritdoc />
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        var status = await health.GetStatusAsync(cancellationToken).ConfigureAwait(false);
        var description = DomainEnumWire.ToWire(status.Status);
        return status.Status == LivenessStatus.Ok
            ? HealthCheckResult.Healthy(
                description,
                new Dictionary<string, object> { [nameof(HostHealthStatus.Runtime)] = status.Runtime })
            : HealthCheckResult.Unhealthy(
                description,
                data: new Dictionary<string, object> { [nameof(HostHealthStatus.Runtime)] = status.Runtime });
    }
}

/// <summary>Writes health check results as JSON matching the MCP health schema.</summary>
public static class HealthCheckJsonResponseWriter
{
    /// <summary>Serializes the health report to JSON on the HTTP response.</summary>
    /// <param name="context">The current HTTP context.</param>
    /// <param name="report">The aggregated health report.</param>
    /// <returns>A task that completes when the response is written.</returns>
    public static Task WriteAsync(HttpContext context, HealthReport report)
    {
        context.Response.ContentType = "application/json";

        var runtime = "dotnet";
        foreach (var entry in report.Entries.Values)
        {
            if (entry.Data.TryGetValue(nameof(HostHealthStatus.Runtime), out var value) && value is not null)
            {
                runtime = value.ToString() ?? runtime;
                break;
            }
        }

        var status = report.Status == AspNetHealthStatus.Healthy
            ? LivenessStatus.Ok
            : LivenessStatus.Unhealthy;
        return context.Response.WriteAsJsonAsync(new HostHealthStatus(status, runtime));
    }
}
