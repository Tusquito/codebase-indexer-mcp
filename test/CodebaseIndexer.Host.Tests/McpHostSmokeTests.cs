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

    /// <summary>Health readiness endpoint returns ok status and dotnet runtime (TEI mocked healthy).</summary>
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

    /// <summary>Liveness endpoint returns 200 without dependency checks.</summary>
    [Fact]
    public async Task Alive_endpoint_returns_ok()
    {
        var client = _factory.CreateClient();
        var response = await client.GetAsync("/alive");
        response.EnsureSuccessStatusCode();
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

    /// <summary>MCP client lists Phase 3 core search/read tools.</summary>
    [Fact]
    public async Task McpClient_lists_core_search_tools()
    {
        await using var mcpClient = await CreateMcpClientAsync();
        var tools = await mcpClient.ListToolsAsync();
        foreach (var name in new[]
                 {
                     "search_codebase",
                     "search_symbols",
                     "get_chunk",
                     "get_file_outline",
                     "get_collection_summary",
                     "list_collections",
                 })
        {
            Assert.Contains(tools, tool => tool.Name == name);
        }
    }

    /// <summary>MCP client lists Phase 4 cross-ref + discovery tools.</summary>
    [Fact]
    public async Task McpClient_lists_discovery_tools()
    {
        await using var mcpClient = await CreateMcpClientAsync();
        var tools = await mcpClient.ListToolsAsync();
        foreach (var name in new[]
                 {
                     "find_cross_references",
                     "map_service_dependencies",
                     "recommend_code",
                     "find_outlier_chunks",
                 })
        {
            Assert.Contains(tools, tool => tool.Name == name);
        }
    }

    /// <summary>find_cross_references error-path contract (no query/symbol/member).</summary>
    [Fact]
    public async Task McpClient_find_cross_references_error_without_inputs()
    {
        await using var mcpClient = await CreateMcpClientAsync();
        var result = await mcpClient.CallToolAsync(
            "find_cross_references",
            new Dictionary<string, object?>());

        var text = result.StructuredContent is { } structured
            ? JsonSerializer.Serialize(structured)
            : result.Content.OfType<TextContentBlock>().FirstOrDefault()?.Text;
        Assert.False(string.IsNullOrWhiteSpace(text));
        using var document = JsonDocument.Parse(text!);
        Assert.True(document.RootElement.TryGetProperty("error", out _));
    }

    /// <summary>map_service_dependencies error-path when fewer than 2 collections.</summary>
    [Fact]
    public async Task McpClient_map_service_dependencies_error_with_one_collection()
    {
        await using var mcpClient = await CreateMcpClientAsync();
        var result = await mcpClient.CallToolAsync(
            "map_service_dependencies",
            new Dictionary<string, object?> { ["collections"] = new[] { "only-one" } });

        var text = result.StructuredContent is { } structured
            ? JsonSerializer.Serialize(structured)
            : result.Content.OfType<TextContentBlock>().FirstOrDefault()?.Text;
        Assert.False(string.IsNullOrWhiteSpace(text));
        using var document = JsonDocument.Parse(text!);
        Assert.True(document.RootElement.TryGetProperty("error", out _));
    }

    /// <summary>list_collections CallTool is wired (Qdrant may be down in unit host).</summary>
    [Fact]
    public async Task McpClient_calls_list_collections()
    {
        await using var mcpClient = await CreateMcpClientAsync();
        var result = await mcpClient.CallToolAsync("list_collections", new Dictionary<string, object?>());

        // Success → JSON array; Qdrant unavailable → IsError with content. Either proves wiring.
        if (result.IsError != true)
        {
            if (result.StructuredContent is { } structured)
            {
                var json = JsonSerializer.Serialize(structured);
                using var document = JsonDocument.Parse(json);
                Assert.Equal(JsonValueKind.Array, document.RootElement.ValueKind);
                return;
            }

            var text = result.Content.OfType<TextContentBlock>().FirstOrDefault()?.Text;
            Assert.False(string.IsNullOrWhiteSpace(text));
            using (var document = JsonDocument.Parse(text!))
            {
                Assert.Equal(JsonValueKind.Array, document.RootElement.ValueKind);
            }

            return;
        }

        Assert.NotEmpty(result.Content);
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
