using CodebaseIndexer.Application.Models;
using CodebaseIndexer.Application.Options;
using CodebaseIndexer.Application.Search;
using CodebaseIndexer.Application.Services;
using CodebaseIndexer.Domain.Models;
using CodebaseIndexer.Domain.Ports;
using MsOptions = Microsoft.Extensions.Options.Options;

namespace CodebaseIndexer.Application.Tests;

/// <summary>Contract tests for find_cross_references orchestration.</summary>
public sealed class CrossReferenceServiceTests
{
    [Fact]
    public async Task FindCrossReferences_errors_when_no_query_symbol_or_member()
    {
        var service = CreateService(new FakeStore());
        var result = await service.FindCrossReferencesAsync();
        var dict = Assert.IsType<Dictionary<string, object?>>(result);
        Assert.Contains("error", dict.Keys);
    }

    [Fact]
    public async Task FindCrossReferences_path_d_uses_qdrant_callers()
    {
        var store = new FakeStore
        {
            Callers =
            [
                new SearchHit(
                    new ChunkId("c1"), 0, "src/A.cs", "csharp", 10, 20,
                    "Caller", "method", "featureService.isEnabled();", "proj-a"),
            ],
        };
        var service = CreateService(store);
        var result = await service.FindCrossReferencesAsync(member: "isEnabled", receiver: "featureService", collections: ["proj-a"]);
        var response = Assert.IsType<CrossReferenceResponse>(result);
        Assert.Equal(1, response.CollectionCount);
        Assert.True(response.FoundIn.ContainsKey("proj-a"));
        Assert.Equal("call_site", response.FoundIn["proj-a"][0].ReferenceType);
        Assert.Equal("isEnabled", store.LastCallerMethod);
        Assert.Equal("featureService", store.LastCallerReceiver);
    }

    [Fact]
    public void UrlExtractors_classify_endpoint_and_http_call()
    {
        var extractors = new UrlExtractors(Array.Empty<string>());
        Assert.Equal(
            "endpoint_definition",
            extractors.ClassifyReference("[HttpGet(\"api/users\")]\npublic IActionResult Get()", "Get", "UsersController.cs"));
        Assert.Equal(
            "http_call",
            extractors.ClassifyReference("await httpClient.GetAsync(\"https://x\");", "x", "Client.cs"));
        Assert.Equal(
            "build_dependency",
            extractors.ClassifyReference("<PackageReference Include=\"Foo\" Version=\"1.0\" />", "", "App.csproj"));
    }

    private static CrossReferenceService CreateService(FakeStore store)
    {
        var search = new SearchService(
            store,
            new FakeDense(),
            new FakeSparse(),
            MsOptions.Create(new EmbeddingOptions
            {
                HybridSearch = true,
                DenseModel = "m",
                SparseModel = "s",
                DenseVectorSize = 2,
                CachePath = "/c",
                PrefetchMultiplier = 5,
                RrfK = 60,
            }));
        return new CrossReferenceService(search, store, new UrlExtractors(Array.Empty<string>()));
    }

    private sealed class FakeDense : IDenseEmbedder
    {
        public int VectorSize => 2;
        public bool IsLoaded => true;
        public Task PreloadAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public void Release() { }
        public Task<IReadOnlyList<IReadOnlyList<float>>> EmbedBatchAsync(IReadOnlyList<string> texts, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<IReadOnlyList<float>>>(texts.Select(_ => (IReadOnlyList<float>)new float[] { 0.1f, 0.2f }).ToArray());
        public Task<IReadOnlyList<IReadOnlyList<float>>> EmbedQueryAsync(IReadOnlyList<string> texts, CancellationToken cancellationToken = default) =>
            EmbedBatchAsync(texts, cancellationToken);
    }

    private sealed class FakeSparse : ISparseEmbedder
    {
        public bool IsLoaded => true;
        public Task PreloadAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public void Release() { }
        public Task<IReadOnlyList<SparseVector>> EmbedBatchAsync(IReadOnlyList<string> texts, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<SparseVector>>(texts.Select(_ => new SparseVector([1u], [1f])).ToArray());
    }

    private sealed class FakeStore : IVectorStore
    {
        public IReadOnlyList<SearchHit> Callers { get; init; } = [];
        public string? LastCallerMethod { get; private set; }
        public string? LastCallerReceiver { get; private set; }

        public ValueTask<bool> CollectionExistsAsync(string collection, CancellationToken cancellationToken = default) => ValueTask.FromResult(true);
        public Task EnsureCollectionAsync(string collection, bool force = false, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task UpsertChunksAsync(string collection, IReadOnlyList<EmbeddedChunk> chunks, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<IReadOnlyList<SearchHit>> SearchAsync(string collection, IReadOnlyList<float> denseVector, SparseVector? sparseVector, int topK, string? language = null, float minScore = 0.5f, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<SearchHit>>([]);
        public Task<ChunkPayload?> GetChunkByIdAsync(string collection, string chunkId, CancellationToken cancellationToken = default) => Task.FromResult<ChunkPayload?>(null);
        public Task<ChunkPayload?> FindChunkByIdAsync(string chunkId, string? collection = null, CancellationToken cancellationToken = default) => Task.FromResult<ChunkPayload?>(null);
        public Task<IReadOnlyList<FileSymbol>> ScrollFileSymbolsAsync(string collection, string relPath, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<FileSymbol>>([]);
        public Task<IReadOnlyList<PayloadRow>> ScrollAllPayloadsAsync(string collection, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<PayloadRow>>([]);
        public Task<IReadOnlyList<CollectionStats>> ListCollectionStatsAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<CollectionStats>>([new CollectionStats("proj-a", 1, 0, "m", "s", "tei", true)]);
        public Task<IReadOnlyList<SearchHit>> FindSymbolInCollectionsAsync(string symbolName, IReadOnlyList<string> collections, int limitPerCollection = 10, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<SearchHit>>([]);
        public Task<IReadOnlyList<string>> ListCollectionsAsync(CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<string>>(["proj-a"]);
        public ValueTask<CollectionStats?> GetCollectionStatsAsync(string collection, CancellationToken cancellationToken = default) => ValueTask.FromResult<CollectionStats?>(null);
        public Task<IReadOnlyDictionary<string, FileMetadata>> GetFileMetadataAsync(string collection, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyDictionary<string, FileMetadata>>(new Dictionary<string, FileMetadata>());
        public Task DeleteByPathsAsync(string collection, IReadOnlyList<string> relPaths, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task SetIndexingAsync(string collection, bool enabled, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task VerifyChunkIdsExistAsync(string collection, IReadOnlyList<string> chunkIds, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<IReadOnlyList<SearchHit>> RecommendAsync(string collection, IReadOnlyList<RecommendExample> positive, IReadOnlyList<RecommendExample>? negative = null, int limit = 5, string? language = null, string? pathGlob = null, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<SearchHit>>([]);
        public Task<IReadOnlyList<SearchHit>> FindOutlierChunksAsync(string collection, IReadOnlyList<string>? contextChunkIds = null, int limit = 5, string? language = null, string? pathGlob = null, float? maxSimilarity = null, int? maxContextSamples = null, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<SearchHit>>([]);
        public Task<IReadOnlyList<SearchHit>> FindCallersInCollectionsAsync(string method, IReadOnlyList<string> collections, string? receiver = null, int limitPerCollection = 10, CancellationToken cancellationToken = default)
        {
            LastCallerMethod = method;
            LastCallerReceiver = receiver;
            return Task.FromResult(Callers);
        }
        public Task<IReadOnlyList<IReadOnlyDictionary<string, string>>> ScrollChunksByPathsAsync(string collection, IReadOnlyList<string> relPaths, IReadOnlyList<string>? payloadFields = null, int limit = 500, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<IReadOnlyDictionary<string, string>>>([]);
    }
}
