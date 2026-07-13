using CodebaseIndexer.Host.Health;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;

namespace CodebaseIndexer.Host;

/// <summary>Maps HTTP endpoints exposed by the MCP host.</summary>
public static class EndpointRouteBuilderExtensions
{
    /// <summary>Maps health checks and the MCP HTTP transport endpoint.</summary>
    /// <param name="app">The web application.</param>
    /// <returns>The same application for chaining.</returns>
    public static WebApplication MapCodebaseIndexerEndpoints(this WebApplication app)
    {
        app.MapHealthChecks("/health", new HealthCheckOptions
        {
            Predicate = registration => registration.Tags.Contains("ready"),
            ResponseWriter = HealthCheckJsonResponseWriter.WriteAsync,
        });

        if (app.Environment.IsDevelopment())
        {
            app.MapHealthChecks("/alive", new HealthCheckOptions
            {
                Predicate = registration => registration.Tags.Contains("live"),
            });
        }

        app.MapMcp("/mcp");
        return app;
    }
}
