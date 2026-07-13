using System.Net;
using System.Text.Json;
using CodebaseIndexer.Infrastructure.Tei;
using Microsoft.Extensions.DependencyInjection;
using Refit;

namespace CodebaseIndexer.Infrastructure.Tests;

public sealed class TeiApiContractTests
{
    [Fact]
    public void EmbeddingsRequest_serializes_openai_shape()
    {
        var json = JsonSerializer.Serialize(new EmbeddingsRequest("model", ["hello"], 768));
        using var document = JsonDocument.Parse(json);
        Assert.True(document.RootElement.TryGetProperty("model", out var model) || document.RootElement.TryGetProperty("Model", out model));
        Assert.Equal("model", model.GetString());
    }

    [Fact]
    public void Refit_client_registers_without_throwing()
    {
        var services = new ServiceCollection();
        services.AddRefitClient<ITeiEmbeddingsApi>()
            .ConfigureHttpClient(c => c.BaseAddress = new Uri("http://localhost:8080/"));

        using var provider = services.BuildServiceProvider();
        var api = provider.GetRequiredService<ITeiEmbeddingsApi>();
        Assert.NotNull(api);
    }
}

public sealed class TeiDenseEmbedderSmokeTests
{
    [Fact]
    public async Task EmbedBatch_returns_empty_for_empty_input()
    {
        var handler = new StubTeiHandler();
        var services = new ServiceCollection();
        services.AddSingleton<ITeiEmbeddingsApi>(sp =>
        {
            var client = new HttpClient(handler) { BaseAddress = new Uri("http://tei/") };
            return RestService.For<ITeiEmbeddingsApi>(client);
        });
        var settings = TestSettingsFactory.Create(denseEmbedVectorSize: 2);
        settings = new Infrastructure.Configuration.Settings
        {
            QdrantUrl = settings.QdrantUrl,
            QdrantTimeoutSeconds = settings.QdrantTimeoutSeconds,
            QdrantCollection = settings.QdrantCollection,
            HybridSearch = settings.HybridSearch,
            DenseEmbedModel = settings.DenseEmbedModel,
            SparseEmbedModel = settings.SparseEmbedModel,
            DenseEmbedVectorSize = settings.DenseEmbedVectorSize,
            TeiUrl = "http://tei/",
            TeiEmbedBatchSize = settings.TeiEmbedBatchSize,
            TeiTimeoutSeconds = settings.TeiTimeoutSeconds,
            MrlDimensions = settings.MrlDimensions,
            QueryInstruction = settings.QueryInstruction,
            NormalizeOutput = settings.NormalizeOutput,
            RerankEnabled = settings.RerankEnabled,
            PayloadIndexes = settings.PayloadIndexes,
            VectorsOnDisk = settings.VectorsOnDisk,
            SparseOnDisk = settings.SparseOnDisk,
            WorkspacePath = settings.WorkspacePath,
            MaxChunkLines = settings.MaxChunkLines,
            ChunkOverlapLines = settings.ChunkOverlapLines,
            BatchSize = settings.BatchSize,
            FlushEvery = settings.FlushEvery,
            UpsertBatch = settings.UpsertBatch,
            ReadaheadBuffer = settings.ReadaheadBuffer,
            HashWorkerDop = settings.HashWorkerDop,
            MaxDenseEmbedTokens = settings.MaxDenseEmbedTokens,
            MaxSparseEmbedTokens = settings.MaxSparseEmbedTokens,
            SparseThreads = settings.SparseThreads,
            SequentialEmbed = settings.SequentialEmbed,
            MemoryPressureWarnPct = settings.MemoryPressureWarnPct,
            MemoryPressureHaltPct = settings.MemoryPressureHaltPct,
            ReleaseModelsAfterIndex = settings.ReleaseModelsAfterIndex,
            ModelIdleTimeoutSeconds = settings.ModelIdleTimeoutSeconds,
            PreloadModels = settings.PreloadModels,
            FastembedCachePath = settings.FastembedCachePath,
            ExcludedDirs = settings.ExcludedDirs,
        };
        services.AddSingleton<Microsoft.Extensions.Options.IOptions<Infrastructure.Configuration.Settings>>(
            _ => Microsoft.Extensions.Options.Options.Create(settings));
        services.AddSingleton<IHttpClientFactory>(_ => new StubHttpClientFactory(handler));
        services.AddSingleton<TeiDenseEmbedder>();
        services.AddLogging();

        using var provider = services.BuildServiceProvider();
        var embedder = provider.GetRequiredService<TeiDenseEmbedder>();
        var result = await embedder.EmbedBatchAsync([]);
        Assert.Empty(result);
    }

    private sealed class StubHttpClientFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler) { BaseAddress = new Uri("http://tei/") };
    }

    private sealed class StubTeiHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("""{"data":[{"embedding":[0.1,0.2],"index":0}]}"""),
            });
    }
}
