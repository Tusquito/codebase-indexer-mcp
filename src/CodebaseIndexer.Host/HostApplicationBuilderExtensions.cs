using CodebaseIndexer.Application;
using CodebaseIndexer.Host.Health;
using CodebaseIndexer.Infrastructure;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Server;

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
            .AddCheck<McpHostHealthCheck>("codebase-indexer", tags: ["ready"])
            .Services
            .AddMcpServer()
            .WithHttpTransport()
            .WithToolsFromAssembly();

        return builder;
    }
}
