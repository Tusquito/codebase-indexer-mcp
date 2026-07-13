using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using ModelContextProtocol.Client;

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
    }

    [Fact]
    public async Task McpClient_lists_stub_tools()
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

        await using var mcpClient = await McpClient.CreateAsync(transport);
        var tools = await mcpClient.ListToolsAsync();
        Assert.Contains(tools, tool => tool.Name == "get_health");
    }

    private sealed record HealthPayload(string Status, string Runtime);
}

public sealed class McpHostWebApplicationFactory : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(Microsoft.AspNetCore.Hosting.IWebHostBuilder builder)
    {
        builder.ConfigureServices(services =>
        {
            services.RemoveAll(typeof(Microsoft.Extensions.Options.IOptions<Infrastructure.Configuration.Settings>));
            services.AddSingleton(
                Microsoft.Extensions.Options.Options.Create(new Infrastructure.Configuration.Settings
                {
                    QdrantUrl = "http://localhost:6333",
                    TeiUrl = "http://localhost:8080",
                    DenseEmbedModel = "test-model",
                    DenseEmbedVectorSize = 768,
                }));
        });
    }
}

public sealed class McpHostFactoryTests
{
    [Fact]
    public void Host_starts_with_in_memory_test_configuration()
    {
        using var factory = new McpHostWebApplicationFactory();
        var client = factory.CreateClient();
        Assert.NotNull(client);
    }
}
