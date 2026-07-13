using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace CodebaseIndexer.Host.Tests;

public sealed class McpHostSmokeTests : IClassFixture<McpHostWebApplicationFactory>
{
    private readonly McpHostWebApplicationFactory _factory;

    public McpHostSmokeTests(McpHostWebApplicationFactory factory) => _factory = factory;

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

    [Fact]
    public async Task McpClient_lists_get_health_tool()
    {
        await using var mcpClient = await CreateMcpClientAsync();
        var tools = await mcpClient.ListToolsAsync();
        Assert.Contains(tools, tool => tool.Name == "get_health");
    }

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

public sealed class McpHostWebApplicationFactory : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(Microsoft.AspNetCore.Hosting.IWebHostBuilder builder)
    {
        builder.ConfigureAppConfiguration((_, config) =>
        {
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                [$"{Infrastructure.Configuration.Settings.SectionName}:QdrantUrl"] = "http://localhost:6333",
                [$"{Infrastructure.Configuration.Settings.SectionName}:QdrantTimeoutSeconds"] = "30",
                [$"{Infrastructure.Configuration.Settings.SectionName}:QdrantCollection"] = "codebase",
                [$"{Infrastructure.Configuration.Settings.SectionName}:HybridSearch"] = "true",
                [$"{Infrastructure.Configuration.Settings.SectionName}:DenseEmbedModel"] = "test-model",
                [$"{Infrastructure.Configuration.Settings.SectionName}:SparseEmbedModel"] = "Qdrant/bm25",
                [$"{Infrastructure.Configuration.Settings.SectionName}:DenseEmbedVectorSize"] = "768",
                [$"{Infrastructure.Configuration.Settings.SectionName}:TeiUrl"] = "http://localhost:8080",
                [$"{Infrastructure.Configuration.Settings.SectionName}:TeiEmbedBatchSize"] = "32",
                [$"{Infrastructure.Configuration.Settings.SectionName}:TeiTimeoutSeconds"] = "120",
                [$"{Infrastructure.Configuration.Settings.SectionName}:QueryInstruction"] = string.Empty,
                [$"{Infrastructure.Configuration.Settings.SectionName}:NormalizeOutput"] = "false",
                [$"{Infrastructure.Configuration.Settings.SectionName}:RerankEnabled"] = "false",
                [$"{Infrastructure.Configuration.Settings.SectionName}:PayloadIndexes"] = "true",
                [$"{Infrastructure.Configuration.Settings.SectionName}:VectorsOnDisk"] = "false",
                [$"{Infrastructure.Configuration.Settings.SectionName}:SparseOnDisk"] = "false",
            });
        });
    }
}

public sealed class McpHostFactoryTests
{
    [Fact]
    public void Host_starts_with_bound_configuration()
    {
        using var factory = new McpHostWebApplicationFactory();
        var client = factory.CreateClient();
        Assert.NotNull(client);
    }
}

public sealed class SettingsValidateOnStartTests
{
    [Fact]
    public void Host_fails_fast_when_required_settings_missing()
    {
        var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.ConfigureAppConfiguration((_, config) =>
            {
                config.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    [$"{Infrastructure.Configuration.Settings.SectionName}:QdrantUrl"] = string.Empty,
                });
            });
        });

        var ex = Assert.ThrowsAny<Exception>(() => factory.CreateClient());
        Assert.Contains("QdrantUrl", ex.ToString(), StringComparison.OrdinalIgnoreCase);
    }
}
