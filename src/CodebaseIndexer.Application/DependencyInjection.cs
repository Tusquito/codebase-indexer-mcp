using CodebaseIndexer.Application.Options;
using CodebaseIndexer.Application.Search;
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
        services.AddMemoryCache();
        services.AddCodebaseIndexerIndexingOptions();
        services.AddSingleton<UrlExtractors>();
        services.AddSingleton<IHealthService, HealthService>();
        services.AddSingleton<IIndexEmbeddingService, IndexEmbeddingService>();
        services.AddSingleton<IIndexCodebaseService, IndexCodebaseService>();
        services.AddSingleton<IIndexJobService, IndexJobService>();
        services.AddSingleton<ISearchService, SearchService>();
        services.AddSingleton<ICollectionQueryService, CollectionQueryService>();
        services.AddSingleton<ICrossReferenceService, CrossReferenceService>();
        services.AddSingleton<IServiceMapService, ServiceMapService>();
        services.AddSingleton<IRecommendService, RecommendService>();
        return services;
    }
}
