using CodebaseIndexer.Application;
using CodebaseIndexer.Application.Options;
using CodebaseIndexer.Host.Health;
using CodebaseIndexer.Host.Tools;
using CodebaseIndexer.Infrastructure;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace CodebaseIndexer.Host;

/// <summary>Registers MCP host services, health checks, and infrastructure.</summary>
public static class HostApplicationBuilderExtensions
{
    /// <summary>Configures the codebase indexer MCP host application.</summary>
    /// <param name="builder">The web application builder.</param>
    /// <returns>The same builder for chaining.</returns>
    public static WebApplicationBuilder AddCodebaseIndexerHost(this WebApplicationBuilder builder)
    {
        builder.AddServiceDefaults();
        builder.Services
            .AddCodebaseIndexerApplication()
            .AddCodebaseIndexerInfrastructure()
            .AddIndexingServices()
            .AddHealthChecks()
            .AddCheck<McpHostHealthCheck>("codebase-indexer", tags: ["ready"]);

        var mcp = builder.Services
            .AddMcpServer()
            .WithHttpTransport()
            .WithTools<HealthTools>()
            .WithTools<IndexTools>()
            .WithTools<SearchTools>()
            .WithTools<ChunkTools>()
            .WithTools<OutlineTools>()
            .WithTools<SummaryTools>()
            .WithTools<CollectionsTools>()
            .WithTools<CrossReferenceTools>()
            .WithTools<ServiceMapTools>();

        // Resolve RecommendEnabled after options bind; gate tool registration like Python main.py.
        var recommendEnabled = builder.Configuration
            .GetSection(DiscoveryOptions.SectionName)
            .GetValue(nameof(DiscoveryOptions.RecommendEnabled), true);
        if (recommendEnabled)
        {
            mcp.WithTools<RecommendTools>()
                .WithTools<OutlierTools>();
        }

        var graphEnabled = builder.Configuration
            .GetSection(GraphOptions.SectionName)
            .GetValue(nameof(GraphOptions.Enabled), false);
        if (graphEnabled)
        {
            mcp.WithTools<ExpandSearchContextTools>();
        }

        return builder;
    }
}
