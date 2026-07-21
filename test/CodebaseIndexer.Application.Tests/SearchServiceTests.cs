using CodebaseIndexer.Application.Options;
using CodebaseIndexer.Application.Services;
using CodebaseIndexer.Domain.Models;
using CodebaseIndexer.Domain.Ports;
using Microsoft.Extensions.Options;
using MsOptions = Microsoft.Extensions.Options.Options;

namespace CodebaseIndexer.Application.Tests;

/// <summary>SearchService unit tests with mocked ports.</summary>
public sealed class SearchServiceTests
{
    /// <summary>Caps top_k at 20 and returns shaped hits.</summary>
    [Fact]
    public async Task SearchCodebase_caps_top_k_and_shapes_results()
    {
        var store = new FakeVectorStore();
        var dense = new FakeDenseEmbedder();
        var sparse = new FakeSparseEmbedder();
        var service = new SearchService(
            store,
            dense,
            sparse,
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

        var result = await service.SearchCodebaseAsync(
            "query",
            topK: 50,
            collection: "demo",
            maxContentChars: 5);

        Assert.Single(result.Results);
        Assert.Equal("abc", result.Results[0].ChunkId);
        Assert.Equal(0.1235, result.Results[0].Score);
        Assert.True(result.Results[0].ContentTruncated);
        Assert.Equal("class", result.Results[0].Content);
        Assert.Equal(20, store.LastTopK);
    }

    /// <summary>ResolveCollections keeps primary first and dedupes.</summary>
    [Fact]
    public void ResolveCollections_dedupes_and_keeps_primary_first()
    {
        var resolved = SearchService.ResolveCollections("a", ["b", "a", "c"]);
        Assert.Equal(["a", "b", "c"], resolved);
    }

    private sealed class FakeDenseEmbedder : IDenseEmbedder
    {
        public int VectorSize => 2;
        public bool IsLoaded => true;
        public Task PreloadAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public void Release() { }

        public Task<IReadOnlyList<IReadOnlyList<float>>> EmbedBatchAsync(
            IReadOnlyList<string> texts,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<IReadOnlyList<float>>>([new float[] { 0.1f, 0.2f }]);

        public Task<IReadOnlyList<IReadOnlyList<float>>> EmbedQueryAsync(
            IReadOnlyList<string> texts,
            CancellationToken cancellationToken = default) =>
            EmbedBatchAsync(texts, cancellationToken);
    }

    private sealed class FakeSparseEmbedder : ISparseEmbedder
    {
        public bool IsLoaded => true;
        public Task PreloadAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public void Release() { }

        public Task<IReadOnlyList<SparseVector>> EmbedBatchAsync(
            IReadOnlyList<string> texts,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<SparseVector>>([new SparseVector([1u], [1f])]);
    }

    private sealed class FakeVectorStore : IVectorStore
    {
        public int LastTopK { get; private set; }

        public ValueTask<bool> CollectionExistsAsync(string collection, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(true);

        public Task EnsureCollectionAsync(string collection, bool force = false, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task UpsertChunksAsync(string collection, IReadOnlyList<EmbeddedChunk> chunks, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task<IReadOnlyList<SearchHit>> SearchAsync(
            string collection,
            IReadOnlyList<float> denseVector,
            SparseVector? sparseVector,
            int topK,
            string? language = null,
            float minScore = 0.5f,
            CancellationToken cancellationToken = default)
        {
            LastTopK = topK;
            IReadOnlyList<SearchHit> hits =
            [
                new SearchHit(
                    new ChunkId("abc"),
                    0.123456,
                    "src/A.cs",
                    "csharp",
                    1,
                    5,
                    "Foo",
                    "class",
                    "class Foo {}",
                    collection),
            ];
            return Task.FromResult(hits);
        }

        public Task<ChunkPayload?> GetChunkByIdAsync(string collection, string chunkId, CancellationToken cancellationToken = default) =>
            Task.FromResult<ChunkPayload?>(null);

        public Task<ChunkPayload?> FindChunkByIdAsync(string chunkId, string? collection = null, CancellationToken cancellationToken = default) =>
            Task.FromResult<ChunkPayload?>(null);

        public Task<IReadOnlyList<FileSymbol>> ScrollFileSymbolsAsync(string collection, string relPath, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<FileSymbol>>([]);

        public Task<IReadOnlyList<PayloadRow>> ScrollAllPayloadsAsync(string collection, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<PayloadRow>>([]);

        public Task<IReadOnlyList<CollectionStats>> ListCollectionStatsAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<CollectionStats>>([]);

        public Task<IReadOnlyList<SearchHit>> FindSymbolInCollectionsAsync(
            string symbolName,
            IReadOnlyList<string> collections,
            int limitPerCollection = 10,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<SearchHit>>([]);

        public Task<IReadOnlyList<string>> ListCollectionsAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<string>>([]);

        public ValueTask<CollectionStats?> GetCollectionStatsAsync(string collection, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<CollectionStats?>(null);

        public Task<IReadOnlyDictionary<string, FileMetadata>> GetFileMetadataAsync(string collection, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyDictionary<string, FileMetadata>>(new Dictionary<string, FileMetadata>());

        public Task DeleteByPathsAsync(string collection, IReadOnlyList<string> relPaths, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task SetIndexingAsync(string collection, bool enabled, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }
}
