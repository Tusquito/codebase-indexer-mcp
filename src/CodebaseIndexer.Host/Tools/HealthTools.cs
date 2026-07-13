using System.ComponentModel;
using CodebaseIndexer.Application.Services;
using ModelContextProtocol.Server;

namespace CodebaseIndexer.Host.Tools;

[McpServerToolType]
public sealed class HealthTools(IHealthService health)
{
    [McpServerTool(Name = "get_health"), Description("Return MCP host health and runtime information.")]
    public async Task<HealthStatus> GetHealthAsync(CancellationToken cancellationToken = default) =>
        await health.GetStatusAsync(cancellationToken).ConfigureAwait(false);
}
