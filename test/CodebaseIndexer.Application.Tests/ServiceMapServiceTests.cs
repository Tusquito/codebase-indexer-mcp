using CodebaseIndexer.Application.Models;
using CodebaseIndexer.Application.Options;
using CodebaseIndexer.Application.Search;
using CodebaseIndexer.Application.Services;
using CodebaseIndexer.Domain.Models;
using CodebaseIndexer.Domain.Ports;
using MsOptions = Microsoft.Extensions.Options.Options;

namespace CodebaseIndexer.Application.Tests;

/// <summary>Contract tests for map_service_dependencies.</summary>
public sealed class ServiceMapServiceTests
{
    [Fact]
    public async Task MapServiceDependencies_errors_with_fewer_than_two_collections()
    {
        var service = CreateService(new FakeStore(["only-one"]));
        var result = await service.MapServiceDependenciesAsync(["only-one"]);
        var dict = Assert.IsType<Dictionary<string, object?>>(result);
        Assert.Contains("error", dict.Keys);
    }

    [Fact]
    public async Task MapServiceDependencies_builds_http_call_edges_from_hits()
    {
        var store = new FakeStore(["svc-a", "svc-b"])
        {
            HitsByCollection = new Dictionary<string, IReadOnlyList<SearchHit>>(StringComparer.Ordinal)
            {
                ["svc-a"] =
                [
                    new SearchHit(
                        new ChunkId("c1"), 0.9, "Clients/ApiClient.cs", "csharp", 1, 20,
                        "Call", "method",
                        "await client.GetAsync(\"/api/users/list\");",
                        "svc-a"),
                ],
                ["svc-b"] =
                [
                    new SearchHit(
                        new ChunkId("c2"), 0.9, "Controllers/UsersController.cs", "csharp", 1, 30,
                        "Get", "method",
                        "[HttpGet(\"api/users/list\")]\npublic IActionResult Get() => Ok();",
                        "svc-b"),
                ],
            },
        };
        var service = CreateService(store);
        var result = await service.MapServiceDependenciesAsync(["svc-a", "svc-b"], topK: 5);
        var response = Assert.IsType<ServiceMapResponse>(result);
        Assert.Contains(response.Edges, e => e.Type == "http_call");
    }

    private static ServiceMapService CreateService(FakeStore store) =>
        new(
            store,
            new FakeDense(),
            new FakeSparse(),
            new FakeColbert(),
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
            MsOptions.Create(new DiscoveryOptions
            {
                RecommendEnabled = true,
                RecommendMaxExamples = 10,
                OutlierMaxContextSamples = 200,
                OutlierMaxSimilarity = 0.55f,
                ServiceUrlKeywords = DiscoveryOptions.DefaultServiceUrlKeywords,
            }),
            new UrlExtractors(Array.Empty<string>()));

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

    private sealed class FakeColbert : IColbertEmbedder
    {
        public int TokenDimension => 128;
        public bool IsLoaded => true;
        public Task PreloadAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public void Release() { }
        public Task<IReadOnlyList<IReadOnlyList<IReadOnlyList<float>>>> EmbedBatchAsync(
            IReadOnlyList<string> texts,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<IReadOnlyList<IReadOnlyList<float>>>>(
                texts.Select(_ => (IReadOnlyList<IReadOnlyList<float>>)new IReadOnlyList<float>[] { new float[] { 0.1f } }).ToArray());
    }

    private sealed class FakeStore : NoOpVectorStore
    {
        private readonly IReadOnlyList<string> _collections;

        public FakeStore(IReadOnlyList<string> collections) => _collections = collections;

        public Dictionary<string, IReadOnlyList<SearchHit>> HitsByCollection { get; init; } = new(StringComparer.Ordinal);

        public override Task<IReadOnlyList<SearchHit>> SearchAsync(
            string collection,
            IReadOnlyList<float> denseVector,
            SparseVector? sparseVector,
            int topK,
            string? language = null,
            float minScore = 0.5f,
            IReadOnlyList<IReadOnlyList<float>>? colbertVector = null,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(HitsByCollection.GetValueOrDefault(collection) ?? Array.Empty<SearchHit>());

        public override Task<IReadOnlyList<CollectionStats>> ListCollectionStatsAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<CollectionStats>>(
                _collections.Select(c => new CollectionStats(c, 1, 0, "m", "s", "tei", true)).ToArray());

        public override Task<IReadOnlyList<string>> ListCollectionsAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(_collections);
    }
}
