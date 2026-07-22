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
        var service = CreateService(store, rerankEnabled: false);

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

    /// <summary>useRerank matrix: null/true use ColBERT when enabled; false skips.</summary>
    [Theory]
    [InlineData(true, null, true)]
    [InlineData(true, true, true)]
    [InlineData(true, false, false)]
    [InlineData(false, null, false)]
    [InlineData(false, true, false)]
    [InlineData(false, false, false)]
    public async Task SearchCodebase_useRerank_matrix(bool rerankEnabled, bool? rerank, bool expectColbert)
    {
        var store = new FakeVectorStore();
        var colbert = new FakeColbertEmbedder();
        var service = CreateService(store, rerankEnabled, colbert);

        await service.SearchCodebaseAsync("query", topK: 5, collection: "demo", rerank: rerank);

        Assert.Equal(expectColbert, colbert.EmbedCalls > 0);
        Assert.Equal(expectColbert, store.LastColbert is not null);
    }

    /// <summary>ResolveCollections keeps primary first and dedupes.</summary>
    [Fact]
    public void ResolveCollections_dedupes_and_keeps_primary_first()
    {
        var resolved = SearchService.ResolveCollections("a", ["b", "a", "c"]);
        Assert.Equal(["a", "b", "c"], resolved);
    }

    /// <summary>ShouldUseRerank matches ADR override semantics.</summary>
    [Theory]
    [InlineData(true, null, true)]
    [InlineData(true, true, true)]
    [InlineData(true, false, false)]
    [InlineData(false, null, false)]
    public void ShouldUseRerank_matrix(bool enabled, bool? rerank, bool expected) =>
        Assert.Equal(expected, SearchService.ShouldUseRerank(enabled, rerank));

    private static SearchService CreateService(
        FakeVectorStore store,
        bool rerankEnabled,
        FakeColbertEmbedder? colbert = null) =>
        new(
            store,
            new FakeDenseEmbedder(),
            new FakeSparseEmbedder(),
            colbert ?? new FakeColbertEmbedder(),
            MsOptions.Create(new EmbeddingOptions
            {
                HybridSearch = true,
                DenseModel = "m",
                SparseModel = "s",
                DenseVectorSize = 2,
                CachePath = "/c",
                PrefetchMultiplier = 5,
                RrfK = 60,
                RerankEnabled = rerankEnabled,
                RerankPrefetch = 100,
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

    private sealed class FakeColbertEmbedder : IColbertEmbedder
    {
        public int EmbedCalls { get; private set; }
        public int TokenDimension => 128;
        public bool IsLoaded => true;
        public Task PreloadAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public void Release() { }

        public Task<IReadOnlyList<IReadOnlyList<IReadOnlyList<float>>>> EmbedBatchAsync(
            IReadOnlyList<string> texts,
            CancellationToken cancellationToken = default)
        {
            EmbedCalls++;
            IReadOnlyList<IReadOnlyList<IReadOnlyList<float>>> result =
                [new IReadOnlyList<float>[] { new float[] { 0.1f } }];
            return Task.FromResult(result);
        }
    }

    private sealed class FakeVectorStore : NoOpVectorStore
    {
        public int LastTopK { get; private set; }
        public IReadOnlyList<IReadOnlyList<float>>? LastColbert { get; private set; }

        public override Task<IReadOnlyList<SearchHit>> SearchAsync(
            string collection,
            IReadOnlyList<float> denseVector,
            SparseVector? sparseVector,
            int topK,
            SourceLanguage? language = null,
            float minScore = 0.5f,
            IReadOnlyList<IReadOnlyList<float>>? colbertVector = null,
            CancellationToken cancellationToken = default)
        {
            LastTopK = topK;
            LastColbert = colbertVector;
            IReadOnlyList<SearchHit> hits =
            [
                new SearchHit(
                    new ChunkId("abc"),
                    0.123456,
                    "src/A.cs",
                    SourceLanguage.CSharp,
                    1,
                    5,
                    "Foo",
                    SymbolType.Class,
                    "class Foo {}",
                    collection),
            ];
            return Task.FromResult(hits);
        }
    }
}
