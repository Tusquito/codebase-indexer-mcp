using CodebaseIndexer.Host.Health;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;

namespace CodebaseIndexer.Host;

public static class EndpointRouteBuilderExtensions
{
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
