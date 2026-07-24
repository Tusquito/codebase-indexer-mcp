using ModelContextProtocol.Client;

namespace CodebaseIndexer.Host.Tests;

/// <summary>expand_search_context is registered only when Graph:Enabled=true.</summary>
public sealed class ExpandSearchContextToolGatingTests
{
    [Test]
    public async Task Expand_search_context_absent_when_graph_disabled()
    {
        await using var factory = new McpHostWebApplicationFactory();
        await using var mcpClient = await CreateMcpClientAsync(factory);
        var tools = await mcpClient.ListToolsAsync();
        await Assert.That(tools).DoesNotContain(tool => tool.Name == "expand_search_context");
    }

    [Test]
    public async Task Expand_search_context_present_when_graph_enabled()
    {
        // Graph:Enabled must be in host configuration — ConfigureAppConfiguration alone is too late
        // for AddCodebaseIndexerHost's WithTools gating (IOptions still sees late overrides).
        await using var factory = new GraphEnabledMcpHostWebApplicationFactory();
        await using var mcpClient = await CreateMcpClientAsync(factory);
        var tools = await mcpClient.ListToolsAsync();
        await Assert.That(tools).Contains(tool => tool.Name == "expand_search_context");
    }

    private static async Task<McpClient> CreateMcpClientAsync(McpHostWebApplicationFactory factory)
    {
        var httpClient = new HttpClient(factory.Server.CreateHandler())
        {
            BaseAddress = new Uri("http://localhost"),
        };

        var transport = new HttpClientTransport(
            new HttpClientTransportOptions
            {
                Endpoint = new Uri("http://localhost/mcp"),
                TransportMode = HttpTransportMode.StreamableHttp,
            },
            httpClient);

        return await McpClient.CreateAsync(transport);
    }
}
