using CodebaseIndexer.Application.Models;
using CodebaseIndexer.Application.Options;
using CodebaseIndexer.Application.Services;
using CodebaseIndexer.Domain.Models;
using CodebaseIndexer.Domain.Ports;
using MsOptions = Microsoft.Extensions.Options.Options;
using CodebaseIndexer.Domain.Results;

namespace CodebaseIndexer.Application.Tests;

/// <summary>Unit tests for expand_search_context orchestration.</summary>
public sealed class ExpandSearchContextServiceTests
{
    [Fact]
    public async Task Expand_collects_seed_chunk_ids_and_clamps_hops()
    {
        var graph = new NoOpGraphStore { Enabled = true };
        var search = new StubSearchService(
        [
            new SearchCodebaseHit(
                "proj/a.py:1", 0.9, "proj", "a.py", "handler", SymbolType.Function, 1, 10, SourceLanguage.Python, "def handler(): ...", null),
        ]);
        var service = CreateService(search, new NoOpVectorStore(), graph, maxHops: 2);

        await service.ExpandSearchContextAsync("find handler", collection: "proj", graphHops: 99);

        Assert.Equal(["proj/a.py:1"], graph.LastExpandChunkIds);
        Assert.Equal(2, graph.LastExpandHops);
        Assert.Equal(200, graph.LastExpandMaxNodes);
    }

    [Fact]
    public async Task Expand_returns_empty_graph_when_disabled()
    {
        var graph = new NoOpGraphStore { Enabled = false };
        var search = new StubSearchService(
        [
            new SearchCodebaseHit(
                "c1", 0.5, "proj", "a.py", "f", SymbolType.Function, 1, 2, SourceLanguage.Python, "x", null),
        ]);
        var service = CreateService(search, new NoOpVectorStore(), graph);

        var result = await service.ExpandSearchContextAsync("q", collection: "proj");
        Assert.True(result.IsSuccess);
        var json = System.Text.Json.JsonSerializer.Serialize(result.Value);
        Assert.Contains("\"nodes\":[]", json);
        Assert.Contains("\"edges\":[]", json);
        Assert.Null(graph.LastExpandChunkIds);
    }

    private static ExpandSearchContextService CreateService(
        ISearchService search,
        IVectorStore store,
        IGraphStore graph,
        int maxHops = 2) =>
        new(
            search,
            store,
            graph,
            MsOptions.Create(new GraphOptions
            {
                Enabled = true,
                Neo4jUri = "bolt://localhost:7687",
                Neo4jUser = "neo4j",
                Neo4jPassword = "pw",
                Neo4jDatabase = "neo4j",
                WriterBatch = 500,
                MaxHops = maxHops,
                MaxNodes = 200,
            }));

    private sealed class StubSearchService : ISearchService
    {
        private readonly IReadOnlyList<SearchCodebaseHit> _hits;

        public StubSearchService(IReadOnlyList<SearchCodebaseHit> hits) => _hits = hits;

        public Task<Result<SearchCodebaseResponse>> SearchCodebaseAsync(
            string query,
            int topK = 5,
            string? collection = null,
            IReadOnlyList<string>? collections = null,
            SourceLanguage? language = null,
            float minScore = 0.5f,
            int? maxContentChars = null,
            bool? rerank = null,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(Result<SearchCodebaseResponse>.Success(
                new SearchCodebaseResponse(_hits, [collection ?? "codebase"], [])));

        public Task<Result<SearchSymbolsResponse>> SearchSymbolsAsync(
            string query,
            int topK = 10,
            string? collection = null,
            IReadOnlyList<string>? collections = null,
            SourceLanguage? language = null,
            float minScore = 0.4f,
            bool? rerank = null,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(Result<SearchSymbolsResponse>.Success(
                new SearchSymbolsResponse([], [collection ?? "codebase"])));
    }
}
