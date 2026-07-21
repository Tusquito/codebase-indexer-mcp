using CodebaseIndexer.Application.Options;

namespace CodebaseIndexer.Host.Tests;

/// <summary>
/// Host factory with <c>Graph:Enabled=true</c> in host configuration so MCP tool gating sees it.
/// </summary>
public sealed class GraphEnabledMcpHostWebApplicationFactory : McpHostWebApplicationFactory
{
    /// <summary>Creates a factory with Graph enabled early enough for WithTools gating.</summary>
    public GraphEnabledMcpHostWebApplicationFactory()
        : base(new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            [$"{GraphOptions.SectionName}:{nameof(GraphOptions.Enabled)}"] = "true",
            [$"{GraphOptions.SectionName}:{nameof(GraphOptions.Neo4jPassword)}"] = "test-pw",
        })
    {
    }
}
