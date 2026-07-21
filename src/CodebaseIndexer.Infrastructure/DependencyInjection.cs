using CodebaseIndexer.Domain.Embedding;
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

/// <summary>Dependency injection registration for the infrastructure layer.</summary>
public static class DependencyInjection
{
    /// <summary>Registers vector store, embedders, chunker, scanner, and TEI HTTP client.</summary>
    /// <param name="services">The service collection.</param>
    /// <returns>The same service collection for chaining.</returns>
    public static IServiceCollection AddCodebaseIndexerInfrastructure(this IServiceCollection services)
    {
        services.AddCodebaseIndexerSettings();

        services.TryAddSingleton<IVectorStore, QdrantVectorStore>();
        services.AddKeyedSingleton<IDenseEmbedder, TeiDenseEmbedder>(EmbedderBackendKeys.Dense.Tei);
        services.AddKeyedSingleton<ISparseEmbedder, OnnxSparseEmbedder>(EmbedderBackendKeys.Sparse.Onnx);
        services.TryAddSingleton<ICodeChunker, TreeSitterChunker>();
        services.TryAddSingleton<IWorkspaceScanner, WorkspaceScanner>();
        services.TryAddSingleton<IMemoryPressureGuard, CgroupMemoryPressureGuard>();

        services.AddRefitClient<ITeiEmbeddingsApi>()
            .ConfigureHttpClient((sp, client) =>
            {
                var tei = sp.GetRequiredService<IOptions<TeiOptions>>().Value;
                client.BaseAddress = new Uri(tei.Url.TrimEnd('/') + "/");
                // Standard resilience TotalRequestTimeout defaults to 30s and would
                // win over HttpClient.Timeout — disable it for slow CPU TEI embeds.
                var seconds = tei.TimeoutSeconds > 0 ? tei.TimeoutSeconds : 600;
                client.Timeout = TimeSpan.FromSeconds(seconds);
            });

        return services;
    }
}
