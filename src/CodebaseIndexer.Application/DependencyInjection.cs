using CodebaseIndexer.Application.Services;
using Microsoft.Extensions.DependencyInjection;

namespace CodebaseIndexer.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddCodebaseIndexerApplication(this IServiceCollection services)
    {
        services.AddSingleton<IHealthService, HealthService>();
        services.AddSingleton<IIndexEmbeddingService, IndexEmbeddingService>();
        services.AddSingleton<IIndexCodebaseService, IndexCodebaseService>();
        services.AddSingleton<IIndexJobService, IndexJobService>();
        return services;
    }
}
