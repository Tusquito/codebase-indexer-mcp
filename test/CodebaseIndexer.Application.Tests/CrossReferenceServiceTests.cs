using CodebaseIndexer.Application.Models;
using CodebaseIndexer.Application.Options;
using CodebaseIndexer.Application.Search;
using CodebaseIndexer.Application.Services;
using CodebaseIndexer.Domain.Models;
using Microsoft.Extensions.Logging.Abstractions;
using MsOptions = Microsoft.Extensions.Options.Options;
using CodebaseIndexer.Domain.Results;

namespace CodebaseIndexer.Application.Tests;

/// <summary>Contract tests for find_cross_references orchestration.</summary>
public sealed class CrossReferenceServiceTests
{
    [Fact]
    public async Task FindCrossReferences_errors_when_no_query_symbol_or_member()
    {
        var service = CreateService(new FakeStore(), new NoOpGraphStore());
        var result = await service.FindCrossReferencesAsync();
        Assert.False(result.IsSuccess);
        Assert.Equal(ErrorKind.Validation, result.Error.Kind);
    }

    [Fact]
    public async Task FindCrossReferences_path_d_uses_qdrant_when_graph_disabled()
    {
        var store = new FakeStore
        {
            Callers =
            [
                new SearchHit(
                    new ChunkId("c1"), 0, "src/A.cs", SourceLanguage.CSharp, 10, 20,
                    "Caller", SymbolType.Method, "featureService.isEnabled();", "proj-a"),
            ],
        };
        var service = CreateService(store, new NoOpGraphStore { Enabled = false });
        var result = await service.FindCrossReferencesAsync(member: "isEnabled", receiver: "featureService", collections: ["proj-a"]);
        Assert.True(result.IsSuccess);
        var response = Assert.IsType<CrossReferenceResponse>(result.Value);
        Assert.Equal(1, response.CollectionCount);
        Assert.True(response.FoundIn.ContainsKey("proj-a"));
        Assert.Equal(ReferenceType.CallSite, response.FoundIn["proj-a"][0].ReferenceType);
        Assert.Equal("isEnabled", store.LastCallerMethod);
        Assert.Equal("featureService", store.LastCallerReceiver);
    }

    [Fact]
    public async Task FindCrossReferences_path_d_uses_neo4j_when_graph_call_sites()
    {
        var store = new FakeStore { GraphCallSites = true };
        var graph = new NoOpGraphStore
        {
            Enabled = true,
            Callers =
            [
                new SearchHit(
                    new ChunkId("neo"), 0, "src/B.cs", SourceLanguage.CSharp, 1, 2,
                    "Caller", SymbolType.Method, "", "proj-a"),
            ],
        };
        var service = CreateService(store, graph);
        var result = await service.FindCrossReferencesAsync(member: "isEnabled", collections: ["proj-a"]);
        Assert.True(result.IsSuccess);
        var response = Assert.IsType<CrossReferenceResponse>(result.Value);
        Assert.Equal(ReferenceType.CallSite, response.FoundIn["proj-a"][0].ReferenceType);
        Assert.Equal("isEnabled", graph.LastCallerMethod);
        Assert.Null(store.LastCallerMethod);
    }

    [Fact]
    public async Task FindCrossReferences_path_d_falls_back_to_qdrant_without_metadata()
    {
        var store = new FakeStore
        {
            GraphCallSites = false,
            Callers =
            [
                new SearchHit(
                    new ChunkId("c1"), 0, "src/A.cs", SourceLanguage.CSharp, 10, 20,
                    "Caller", SymbolType.Method, "x", "proj-a"),
            ],
        };
        var graph = new NoOpGraphStore { Enabled = true };
        var service = CreateService(store, graph);
        var result = await service.FindCrossReferencesAsync(member: "isEnabled", collections: ["proj-a"]);
        Assert.True(result.IsSuccess);
        var response = Assert.IsType<CrossReferenceResponse>(result.Value);
        Assert.Equal(ReferenceType.CallSite, response.FoundIn["proj-a"][0].ReferenceType);
        Assert.Equal("isEnabled", store.LastCallerMethod);
        Assert.Null(graph.LastCallerMethod);
    }

    [Fact]
    public void UrlExtractors_classify_endpoint_and_http_call()
    {
        var extractors = new UrlExtractors(Array.Empty<string>());
        Assert.Equal(
            ReferenceType.EndpointDefinition,
            extractors.ClassifyReference("[HttpGet(\"api/users\")]\npublic IActionResult Get()", "Get", "UsersController.cs"));
        Assert.Equal(
            ReferenceType.HttpCall,
            extractors.ClassifyReference("await httpClient.GetAsync(\"https://x\");", "x", "Client.cs"));
        Assert.Equal(
            ReferenceType.BuildDependency,
            extractors.ClassifyReference("<PackageReference Include=\"Foo\" Version=\"1.0\" />", "", "App.csproj"));
    }

    private static CrossReferenceService CreateService(FakeStore store, NoOpGraphStore graph)
    {
        var search = new SearchService(
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
        return new CrossReferenceService(
            search, store, graph, new UrlExtractors(Array.Empty<string>()), NullLogger<CrossReferenceService>.Instance);
    }

    private sealed class FakeDense : Domain.Ports.IDenseEmbedder
    {
        public int VectorSize => 2;
        public bool IsLoaded => true;
        public Task<Result> PreloadAsync(CancellationToken cancellationToken = default) => Task.FromResult(Result.Success());
        public void Release() { }
        public Task<Result<IReadOnlyList<IReadOnlyList<float>>>> EmbedBatchAsync(IReadOnlyList<string> texts, CancellationToken cancellationToken = default) =>
            Task.FromResult(Result<IReadOnlyList<IReadOnlyList<float>>>.Success(
                texts.Select(_ => (IReadOnlyList<float>)new float[] { 0.1f, 0.2f }).ToArray()));
        public Task<Result<IReadOnlyList<IReadOnlyList<float>>>> EmbedQueryAsync(IReadOnlyList<string> texts, CancellationToken cancellationToken = default) =>
            EmbedBatchAsync(texts, cancellationToken);
    }

    private sealed class FakeSparse : Domain.Ports.ISparseEmbedder
    {
        public bool IsLoaded => true;
        public Task<Result> PreloadAsync(CancellationToken cancellationToken = default) => Task.FromResult(Result.Success());
        public void Release() { }
        public Task<Result<IReadOnlyList<SparseVector>>> EmbedBatchAsync(IReadOnlyList<string> texts, CancellationToken cancellationToken = default) =>
            Task.FromResult(Result<IReadOnlyList<SparseVector>>.Success(
                texts.Select(_ => new SparseVector([1u], [1f])).ToArray()));
    }

    private sealed class FakeColbert : Domain.Ports.IColbertEmbedder
    {
        public int TokenDimension => 128;
        public bool IsLoaded => true;
        public Task<Result> PreloadAsync(CancellationToken cancellationToken = default) => Task.FromResult(Result.Success());
        public void Release() { }
        public Task<Result<IReadOnlyList<IReadOnlyList<IReadOnlyList<float>>>>> EmbedBatchAsync(
            IReadOnlyList<string> texts,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(Result<IReadOnlyList<IReadOnlyList<IReadOnlyList<float>>>>.Success(
                texts.Select(_ => (IReadOnlyList<IReadOnlyList<float>>)new IReadOnlyList<float>[] { new float[] { 0.1f } }).ToArray()));
    }

    private sealed class FakeStore : NoOpVectorStore
    {
        public IReadOnlyList<SearchHit> Callers { get; init; } = [];
        public bool GraphCallSites { get; init; }
        public string? LastCallerMethod { get; private set; }
        public string? LastCallerReceiver { get; private set; }

        public override Task<Result<IReadOnlyList<CollectionStats>>> ListCollectionStatsAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(Result<IReadOnlyList<CollectionStats>>.Success(
                [new CollectionStats("proj-a", 1, 0, "m", "s", "tei", true)]));

        public override Task<Result<IReadOnlyList<string>>> ListCollectionsAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(Result<IReadOnlyList<string>>.Success(["proj-a"]));

        public override ValueTask<bool> CollectionHasGraphCallSitesAsync(
            string collection,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(GraphCallSites);

        public override Task<Result<IReadOnlyList<SearchHit>>> FindCallersInCollectionsAsync(
            string method,
            IReadOnlyList<string> collections,
            string? receiver = null,
            int limitPerCollection = 10,
            CancellationToken cancellationToken = default)
        {
            LastCallerMethod = method;
            LastCallerReceiver = receiver;
            return Task.FromResult(Result<IReadOnlyList<SearchHit>>.Success(Callers));
        }
    }
}
