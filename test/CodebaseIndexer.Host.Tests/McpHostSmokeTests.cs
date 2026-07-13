using System.Net.Http.Json;
using System.Text.Json;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace CodebaseIndexer.Host.Tests;

/// <summary>End-to-end smoke tests for the MCP host HTTP and tool surface.</summary>
public sealed class McpHostSmokeTests : IClassFixture<McpHostWebApplicationFactory>
{
    private readonly McpHostWebApplicationFactory _factory;

    /// <summary>Initializes tests with the shared web application factory.</summary>
    public McpHostSmokeTests(McpHostWebApplicationFactory factory) => _factory = factory;

    /// <summary>Health endpoint returns ok status and dotnet runtime.</summary>
    [Fact]
    public async Task Health_endpoint_returns_ok()
    {
        var client = _factory.CreateClient();
        var response = await client.GetAsync("/health");
        response.EnsureSuccessStatusCode();
        var payload = await response.Content.ReadFromJsonAsync<HealthPayload>();
        Assert.NotNull(payload);
        Assert.Equal("ok", payload!.Status);
        Assert.Equal("dotnet", payload.Runtime);
    }

    /// <summary>MCP client lists the get_health tool.</summary>
    [Fact]
    public async Task McpClient_lists_get_health_tool()
    {
        await using var mcpClient = await CreateMcpClientAsync();
        var tools = await mcpClient.ListToolsAsync();
        Assert.Contains(tools, tool => tool.Name == "get_health");
    }

    /// <summary>MCP client invokes get_health and receives expected payload.</summary>
    [Fact]
    public async Task McpClient_calls_get_health()
    {
        await using var mcpClient = await CreateMcpClientAsync();
        var result = await mcpClient.CallToolAsync("get_health", new Dictionary<string, object?>());

        if (result.StructuredContent is { } structured)
        {
            var json = JsonSerializer.Serialize(structured);
            using var document = JsonDocument.Parse(json);
            Assert.Equal("ok", document.RootElement.GetProperty("status").GetString());
            Assert.Equal("dotnet", document.RootElement.GetProperty("runtime").GetString());
            return;
        }

        var text = result.Content.OfType<TextContentBlock>().FirstOrDefault()?.Text;
        Assert.False(string.IsNullOrWhiteSpace(text));
        using (var document = JsonDocument.Parse(text!))
        {
            Assert.Equal("ok", document.RootElement.GetProperty("status").GetString());
            Assert.Equal("dotnet", document.RootElement.GetProperty("runtime").GetString());
        }
    }

    /// <summary>MCP client lists all index-related tools.</summary>
    [Fact]
    public async Task McpClient_lists_index_tools()
    {
        await using var mcpClient = await CreateMcpClientAsync();
        var tools = await mcpClient.ListToolsAsync();
        foreach (var name in new[] { "index_codebase", "index_status", "stop_indexing", "index_all" })
        {
            Assert.Contains(tools, tool => tool.Name == name);
        }
    }

    private async Task<McpClient> CreateMcpClientAsync()
    {
        var httpClient = new HttpClient(_factory.Server.CreateHandler())
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

    private sealed record HealthPayload(string Status, string Runtime);
}
