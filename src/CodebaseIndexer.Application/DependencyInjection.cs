using CodebaseIndexer.Application.Options;
using CodebaseIndexer.Application.Services;
using Microsoft.Extensions.DependencyInjection;

namespace CodebaseIndexer.Application;

/// <summary>Dependency injection registration for the application layer.</summary>
public static class DependencyInjection
{
    /// <summary>Registers application services and options.</summary>
    /// <param name="services">The service collection.</param>
    /// <returns>The same service collection for chaining.</returns>
    public static IServiceCollection AddCodebaseIndexerApplication(this IServiceCollection services)
    {
        services.AddCodebaseIndexerIndexingOptions();
        services.AddSingleton<IHealthService, HealthService>();
        services.AddSingleton<IIndexEmbeddingService, IndexEmbeddingService>();
        services.AddSingleton<IIndexCodebaseService, IndexCodebaseService>();
        services.AddSingleton<IIndexJobService, IndexJobService>();
        return services;
    }
}
