using CodebaseIndexer.Application.Options;
using CodebaseIndexer.Application.Services;
using CodebaseIndexer.Domain.Models;
using CodebaseIndexer.Domain.Ports;
using Microsoft.Extensions.Logging.Abstractions;
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
            }),
            MsOptions.Create(new GraphOptions
            {
                Enabled = false,
                Neo4jUri = "bolt://localhost:7687",
                Neo4jUser = "neo4j",
                Neo4jPassword = "",
                Neo4jDatabase = "neo4j",
                WriterBatch = 500,
                MaxHops = 2,
                MaxNodes = 200,
            }),
            NullLogger<SearchService>.Instance);

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

    private sealed class FakeVectorStore : NoOpVectorStore
    {
        public int LastTopK { get; private set; }

        public override Task<IReadOnlyList<SearchHit>> SearchAsync(
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
    }
}
