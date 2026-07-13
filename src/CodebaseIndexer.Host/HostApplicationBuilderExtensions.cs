using CodebaseIndexer.Application;
using CodebaseIndexer.Host.Health;
using CodebaseIndexer.Infrastructure;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Server;

namespace CodebaseIndexer.Host;

public static class HostApplicationBuilderExtensions
{
    public static WebApplicationBuilder AddCodebaseIndexerHost(this WebApplicationBuilder builder)
    {
        builder.AddServiceDefaults();
        builder.Services
            .AddCodebaseIndexerApplication()
            .AddCodebaseIndexerInfrastructure()
            .AddHealthChecks()
            .AddCheck<McpHostHealthCheck>("codebase-indexer", tags: ["ready"])
            .Services
            .AddMcpServer()
            .WithHttpTransport()
            .WithToolsFromAssembly();

        return builder;
    }
}
