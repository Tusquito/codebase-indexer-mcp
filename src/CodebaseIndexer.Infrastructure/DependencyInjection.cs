using CodebaseIndexer.Domain.Ports;
using CodebaseIndexer.Infrastructure.Configuration;
using CodebaseIndexer.Infrastructure.Embedding;
using CodebaseIndexer.Infrastructure.Indexing;
using CodebaseIndexer.Infrastructure.Memory;
using CodebaseIndexer.Infrastructure.Qdrant;
using CodebaseIndexer.Infrastructure.Tei;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Refit;

namespace CodebaseIndexer.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddCodebaseIndexerInfrastructure(this IServiceCollection services)
    {
        services.AddCodebaseIndexerSettings();

        services.TryAddSingleton<IVectorStore, QdrantVectorStore>();
        services.TryAddSingleton<IDenseEmbedder, TeiDenseEmbedder>();
        services.TryAddSingleton<ISparseEmbedder, OnnxSparseEmbedder>();
        services.TryAddSingleton<ICodeChunker, TreeSitterChunker>();
        services.TryAddSingleton<IWorkspaceScanner, WorkspaceScanner>();
        services.TryAddSingleton<IMemoryPressureGuard, CgroupMemoryPressureGuard>();

        services.AddRefitClient<ITeiEmbeddingsApi>()
            .ConfigureHttpClient((sp, client) =>
            {
                var settings = sp.GetRequiredService<IOptions<Settings>>().Value;
                client.BaseAddress = new Uri(settings.TeiUrl.TrimEnd('/') + "/");
                client.Timeout = TimeSpan.FromSeconds(settings.TeiTimeoutSeconds);
            })
            .AddStandardResilienceHandler();

        return services;
    }
}
