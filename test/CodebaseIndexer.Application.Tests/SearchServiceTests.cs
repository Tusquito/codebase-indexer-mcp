using CodebaseIndexer.Application.Models;
using CodebaseIndexer.Application.Options;
using CodebaseIndexer.Application.Services;
using CodebaseIndexer.Domain.Models;
using CodebaseIndexer.Domain.Ports;
using CodebaseIndexer.Domain.Results;
using Microsoft.Extensions.Logging.Abstractions;
using MsOptions = Microsoft.Extensions.Options.Options;

namespace CodebaseIndexer.Application.Tests;

public sealed class SearchServiceTests
{
    [Fact]
    public async Task SearchCodebase_returns_empty_hits_as_success()
    {
        var service = CreateService();
        var result = await service.SearchCodebaseAsync("query", topK: 5, collection: "demo");
        Assert.True(result.IsSuccess);
        Assert.Empty(result.Value.Results);
    }

    [Fact]
    public async Task SearchSymbols_returns_empty_hits_as_success()
    {
        var service = CreateService();
        var result = await service.SearchSymbolsAsync("query", topK: 5, collection: "demo");
        Assert.True(result.IsSuccess);
        Assert.Empty(result.Value.Results);
    }

    [Fact]
    public async Task ShouldUseRerank_respects_override()
    {
        Assert.True(SearchService.ShouldUseRerank(true, null));
        Assert.False(SearchService.ShouldUseRerank(true, false));
        Assert.False(SearchService.ShouldUseRerank(false, true));
    }

    [Fact]
    public async Task SearchCodebase_dense_embed_failure_short_circuits_without_store_search()
    {
        var store = new FakeVectorStore();
        var dense = new FakeDenseEmbedder
        {
            QueryFailure = new Error(ErrorKind.Dependency, EmbedErrorCodes.Tei, "TEI unreachable"),
        };
        var service = CreateService(store: store, dense: dense);

        var result = await service.SearchCodebaseAsync("query", topK: 5, collection: "demo");

        Assert.False(result.IsSuccess);
        Assert.Equal(ErrorKind.Dependency, result.Error.Kind);
        Assert.Equal(EmbedErrorCodes.Tei, result.Error.Code);
        Assert.Equal(0, store.SearchCalls);
    }

    private static SearchService CreateService(
        FakeColbertEmbedder? colbert = null,
        FakeVectorStore? store = null,
        FakeDenseEmbedder? dense = null) =>
        new(
            store ?? new FakeVectorStore(),
            dense ?? new FakeDenseEmbedder(),
            new FakeSparseEmbedder(),
            colbert ?? new FakeColbertEmbedder(),
            MsOptions.Create(new EmbeddingOptions
            {
                HybridSearch = true,
                RerankEnabled = false,
                DenseVectorSize = 2,
                DenseModel = "test",
                SparseModel = "test",
                PrefetchMultiplier = 2,
                RrfK = 60,
            }),
            MsOptions.Create(new GraphOptions
            {
                Enabled = false,
                Neo4jUri = "bolt://localhost:7687",
                Neo4jUser = "neo4j",
                Neo4jPassword = "password",
                Neo4jDatabase = "neo4j",
                WriterBatch = 500,
                MaxHops = 2,
                MaxNodes = 200,
            }),
            NullLogger<SearchService>.Instance);

    private sealed class FakeDenseEmbedder : IDenseEmbedder
    {
        public Error? QueryFailure { get; init; }
        public int VectorSize => 2;
        public bool IsLoaded => true;
        public Task<Result> PreloadAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(Result.Success());
        public void Release() { }

        public Task<Result<IReadOnlyList<IReadOnlyList<float>>>> EmbedBatchAsync(
            IReadOnlyList<string> texts,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(Result<IReadOnlyList<IReadOnlyList<float>>>.Success(
                texts.Select(_ => (IReadOnlyList<float>)new float[] { 0.1f, 0.2f }).ToArray()));

        public Task<Result<IReadOnlyList<IReadOnlyList<float>>>> EmbedQueryAsync(
            IReadOnlyList<string> texts,
            CancellationToken cancellationToken = default)
        {
            if (QueryFailure is not null)
            {
                return Task.FromResult(Result<IReadOnlyList<IReadOnlyList<float>>>.Failure(QueryFailure));
            }

            return EmbedBatchAsync(texts, cancellationToken);
        }
    }

    private sealed class FakeSparseEmbedder : ISparseEmbedder
    {
        public bool IsLoaded => true;
        public Task<Result> PreloadAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(Result.Success());
        public void Release() { }

        public Task<Result<IReadOnlyList<SparseVector>>> EmbedBatchAsync(
            IReadOnlyList<string> texts,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(Result<IReadOnlyList<SparseVector>>.Success(
                texts.Select(_ => new SparseVector([1u], [1f])).ToArray()));
    }

    private sealed class FakeColbertEmbedder : IColbertEmbedder
    {
        public int EmbedCalls { get; private set; }
        public int TokenDimension => 128;
        public bool IsLoaded => true;
        public Task<Result> PreloadAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(Result.Success());
        public void Release() { }

        public Task<Result<IReadOnlyList<IReadOnlyList<IReadOnlyList<float>>>>> EmbedBatchAsync(
            IReadOnlyList<string> texts,
            CancellationToken cancellationToken = default)
        {
            EmbedCalls++;
            var result = texts
                .Select(_ => (IReadOnlyList<IReadOnlyList<float>>)new IReadOnlyList<float>[] { new float[] { 0.1f } })
                .ToArray();
            return Task.FromResult(Result<IReadOnlyList<IReadOnlyList<IReadOnlyList<float>>>>.Success(result));
        }
    }

    private sealed class FakeVectorStore : NoOpVectorStore
    {
        public int SearchCalls { get; private set; }

        public override Task<Result<IReadOnlyList<SearchHit>>> SearchAsync(
            string collection,
            IReadOnlyList<float> denseVector,
            SparseVector? sparseVector,
            int topK,
            SourceLanguage? language = null,
            float minScore = 0.5f,
            IReadOnlyList<IReadOnlyList<float>>? colbertVector = null,
            CancellationToken cancellationToken = default)
        {
            SearchCalls++;
            return Task.FromResult(Result<IReadOnlyList<SearchHit>>.Success([]));
        }
    }
}
