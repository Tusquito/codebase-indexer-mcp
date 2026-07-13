using System.ComponentModel;
using CodebaseIndexer.Application.Services;
using ModelContextProtocol.Server;

namespace CodebaseIndexer.Host.Tools;

/// <summary>MCP tools for host health reporting.</summary>
[McpServerToolType]
public sealed class HealthTools(IHealthService health)
{
    /// <summary>Returns MCP host health and runtime information.</summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Current host health status.</returns>
    [McpServerTool(Name = "get_health"), Description("Return MCP host health and runtime information.")]
    public async Task<HealthStatus> GetHealthAsync(CancellationToken cancellationToken = default) =>
        await health.GetStatusAsync(cancellationToken).ConfigureAwait(false);
}
