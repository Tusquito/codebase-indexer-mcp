using CodebaseIndexer.Application.Models;
using CodebaseIndexer.Application.Options;
using CodebaseIndexer.Application.Services;
using CodebaseIndexer.Domain.Models;
using CodebaseIndexer.Domain.Ports;
using MsOptions = Microsoft.Extensions.Options.Options;

namespace CodebaseIndexer.Application.Tests;

/// <summary>Port of test_recommend_tool.py / outlier validation.</summary>
public sealed class RecommendServiceTests
{
    [Fact]
    public async Task RecommendCode_requires_positive_example()
    {
        var service = CreateService(new FakeStore());
        var ex = await Assert.ThrowsAsync<ArgumentException>(() =>
            service.RecommendCodeAsync("proj"));
        Assert.Contains("positive", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task RecommendCode_caps_limit_at_20()
    {
        var store = new FakeStore();
        var service = CreateService(store);
        await service.RecommendCodeAsync("proj", positiveChunkIds: ["a"], limit: 50);
        Assert.Equal(20, store.LastRecommendLimit);
    }

    [Fact]
    public async Task RecommendCode_enforces_max_examples()
    {
        var service = CreateService(new FakeStore(), recommendMaxExamples: 2);
        var ex = await Assert.ThrowsAsync<ArgumentException>(() =>
            service.RecommendCodeAsync(
                "proj",
                positiveChunkIds: ["a", "b"],
                negativeChunkIds: ["c"]));
        Assert.Contains("RECOMMEND_MAX_EXAMPLES", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RecommendCode_verifies_ids_embeds_and_shapes_results()
    {
        var store = new FakeStore
        {
            RecommendHits =
            [
                new SearchHit(
                    new ChunkId("src/h.py:1"), 0.9, "src/h.py", "python", 1, 10,
                    "handler", "function", "def handler(): ...", "proj"),
            ],
        };
        var dense = new FakeDense();
        var service = new RecommendService(
            store,
            dense,
            MsOptions.Create(new DiscoveryOptions
            {
                RecommendEnabled = true,
                RecommendMaxExamples = 10,
                OutlierMaxContextSamples = 200,
                OutlierMaxSimilarity = 0.55f,
            }));

        var result = await service.RecommendCodeAsync(
            "proj",
            positiveChunkIds: ["pos"],
            positiveQuery: "handler pattern",
            negativeChunkIds: ["neg"],
            negativeQuery: "test utilities",
            pathGlob: "src/*.py");

        Assert.Equal(["pos", "neg"], store.VerifiedIds);
        Assert.Equal(["handler pattern", "test utilities"], dense.LastTexts);
        Assert.Equal(2, store.LastPositiveCount);
        Assert.Equal(2, store.LastNegativeCount);
        var response = Assert.IsType<RecommendCodeResponse>(result);
        Assert.Single(response.Results);
        Assert.Equal(2, response.PositiveExamples);
        Assert.Equal(2, response.NegativeExamples);
    }

    [Fact]
    public async Task FindOutlierChunks_rejects_invalid_max_similarity()
    {
        var service = CreateService(new FakeStore());
        await Assert.ThrowsAsync<ArgumentException>(() =>
            service.FindOutlierChunksAsync("proj", maxSimilarity: 1.5f));
    }

    private static RecommendService CreateService(FakeStore store, int recommendMaxExamples = 10) =>
        new(
            store,
            new FakeDense(),
            MsOptions.Create(new DiscoveryOptions
            {
                RecommendEnabled = true,
                RecommendMaxExamples = recommendMaxExamples,
                OutlierMaxContextSamples = 200,
                OutlierMaxSimilarity = 0.55f,
            }));

    private sealed class FakeDense : IDenseEmbedder
    {
        public IReadOnlyList<string>? LastTexts { get; private set; }
        public int VectorSize => 2;
        public bool IsLoaded => true;
        public Task PreloadAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public void Release() { }
        public Task<IReadOnlyList<IReadOnlyList<float>>> EmbedBatchAsync(IReadOnlyList<string> texts, CancellationToken cancellationToken = default)
        {
            LastTexts = texts.ToArray();
            return Task.FromResult<IReadOnlyList<IReadOnlyList<float>>>(
                texts.Select(_ => (IReadOnlyList<float>)new float[] { 0.2f, 0.3f }).ToArray());
        }
        public Task<IReadOnlyList<IReadOnlyList<float>>> EmbedQueryAsync(IReadOnlyList<string> texts, CancellationToken cancellationToken = default) =>
            EmbedBatchAsync(texts, cancellationToken);
    }

    private sealed class FakeStore : IVectorStore
    {
        public IReadOnlyList<string>? VerifiedIds { get; private set; }
        public int LastRecommendLimit { get; private set; }
        public int LastPositiveCount { get; private set; }
        public int LastNegativeCount { get; private set; }
        public IReadOnlyList<SearchHit> RecommendHits { get; init; } = [];

        public ValueTask<bool> CollectionExistsAsync(string collection, CancellationToken cancellationToken = default) => ValueTask.FromResult(true);
        public Task EnsureCollectionAsync(string collection, bool force = false, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task UpsertChunksAsync(string collection, IReadOnlyList<EmbeddedChunk> chunks, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<IReadOnlyList<SearchHit>> SearchAsync(string collection, IReadOnlyList<float> denseVector, SparseVector? sparseVector, int topK, string? language = null, float minScore = 0.5f, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<SearchHit>>([]);
        public Task<ChunkPayload?> GetChunkByIdAsync(string collection, string chunkId, CancellationToken cancellationToken = default) => Task.FromResult<ChunkPayload?>(null);
        public Task<ChunkPayload?> FindChunkByIdAsync(string chunkId, string? collection = null, CancellationToken cancellationToken = default) => Task.FromResult<ChunkPayload?>(null);
        public Task<IReadOnlyList<FileSymbol>> ScrollFileSymbolsAsync(string collection, string relPath, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<FileSymbol>>([]);
        public Task<IReadOnlyList<PayloadRow>> ScrollAllPayloadsAsync(string collection, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<PayloadRow>>([]);
        public Task<IReadOnlyList<CollectionStats>> ListCollectionStatsAsync(CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<CollectionStats>>([]);
        public Task<IReadOnlyList<SearchHit>> FindSymbolInCollectionsAsync(string symbolName, IReadOnlyList<string> collections, int limitPerCollection = 10, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<SearchHit>>([]);
        public Task<IReadOnlyList<string>> ListCollectionsAsync(CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<string>>([]);
        public ValueTask<CollectionStats?> GetCollectionStatsAsync(string collection, CancellationToken cancellationToken = default) => ValueTask.FromResult<CollectionStats?>(null);
        public Task<IReadOnlyDictionary<string, FileMetadata>> GetFileMetadataAsync(string collection, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyDictionary<string, FileMetadata>>(new Dictionary<string, FileMetadata>());
        public Task DeleteByPathsAsync(string collection, IReadOnlyList<string> relPaths, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task SetIndexingAsync(string collection, bool enabled, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task VerifyChunkIdsExistAsync(string collection, IReadOnlyList<string> chunkIds, CancellationToken cancellationToken = default)
        {
            VerifiedIds = chunkIds.ToArray();
            return Task.CompletedTask;
        }
        public Task<IReadOnlyList<SearchHit>> RecommendAsync(string collection, IReadOnlyList<RecommendExample> positive, IReadOnlyList<RecommendExample>? negative = null, int limit = 5, string? language = null, string? pathGlob = null, CancellationToken cancellationToken = default)
        {
            LastRecommendLimit = limit;
            LastPositiveCount = positive.Count;
            LastNegativeCount = negative?.Count ?? 0;
            return Task.FromResult(RecommendHits);
        }
        public Task<IReadOnlyList<SearchHit>> FindOutlierChunksAsync(string collection, IReadOnlyList<string>? contextChunkIds = null, int limit = 5, string? language = null, string? pathGlob = null, float? maxSimilarity = null, int? maxContextSamples = null, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<SearchHit>>([]);
        public Task<IReadOnlyList<SearchHit>> FindCallersInCollectionsAsync(string method, IReadOnlyList<string> collections, string? receiver = null, int limitPerCollection = 10, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<SearchHit>>([]);
        public Task<IReadOnlyList<IReadOnlyDictionary<string, string>>> ScrollChunksByPathsAsync(string collection, IReadOnlyList<string> relPaths, IReadOnlyList<string>? payloadFields = null, int limit = 500, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<IReadOnlyDictionary<string, string>>>([]);
    }
}
