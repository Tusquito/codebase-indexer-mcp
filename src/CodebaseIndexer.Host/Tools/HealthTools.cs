using System.ComponentModel;
using CodebaseIndexer.Application.Services;
using ModelContextProtocol.Server;

namespace CodebaseIndexer.Host.Tools;

[McpServerToolType]
public sealed class HealthTools(IHealthService health)
{
    [McpServerTool, Description("Return MCP host health and runtime information.")]
    public async Task<HealthStatus> GetHealth(CancellationToken cancellationToken = default) =>
        await health.GetStatusAsync(cancellationToken).ConfigureAwait(false);
}
