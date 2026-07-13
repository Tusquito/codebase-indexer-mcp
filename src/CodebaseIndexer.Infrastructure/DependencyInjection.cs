using CodebaseIndexer.Domain.Ports;
using CodebaseIndexer.Infrastructure.Configuration;
using CodebaseIndexer.Infrastructure.Qdrant;
using CodebaseIndexer.Infrastructure.Tei;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Refit;

namespace CodebaseIndexer.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddCodebaseIndexerInfrastructure(this IServiceCollection services)
    {
        services.AddCodebaseIndexerSettings();

        services.TryAddSingleton<IVectorStore, QdrantVectorStore>();
        services.TryAddSingleton<IDenseEmbedder, TeiDenseEmbedder>();

        services.AddRefitClient<ITeiEmbeddingsApi>()
            .ConfigureHttpClient((sp, client) =>
            {
                var settings = sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<Settings>>().Value;
                client.BaseAddress = new Uri(settings.TeiUrl.TrimEnd('/') + "/");
                client.Timeout = TimeSpan.FromSeconds(settings.TeiTimeoutSeconds);
            })
            .AddStandardResilienceHandler();

        services.AddHttpClient(nameof(TeiDenseEmbedder));

        return services;
    }
}
