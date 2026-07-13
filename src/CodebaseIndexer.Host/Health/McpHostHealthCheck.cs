using CodebaseIndexer.Application.Services;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace CodebaseIndexer.Host.Health;

public sealed class McpHostHealthCheck(IHealthService health) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        var status = await health.GetStatusAsync(cancellationToken).ConfigureAwait(false);
        return HealthCheckResult.Healthy(
            status.Status,
            new Dictionary<string, object> { ["runtime"] = status.Runtime });
    }
}

public static class HealthCheckJsonResponseWriter
{
    public static Task WriteAsync(HttpContext context, HealthReport report)
    {
        context.Response.ContentType = "application/json";

        var runtime = "dotnet";
        foreach (var entry in report.Entries.Values)
        {
            if (entry.Data.TryGetValue("runtime", out var value) && value is not null)
            {
                runtime = value.ToString() ?? runtime;
                break;
            }
        }

        var status = report.Status == Microsoft.Extensions.Diagnostics.HealthChecks.HealthStatus.Healthy ? "ok" : "unhealthy";
        return context.Response.WriteAsJsonAsync(new { status, runtime });
    }
}
