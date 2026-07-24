using CodebaseIndexer.Application.Models;
using CodebaseIndexer.Application.Options;
using CodebaseIndexer.Application.Services;
using CodebaseIndexer.Domain.Models;
using CodebaseIndexer.Domain.Ports;
using CodebaseIndexer.Domain.Results;
using MsOptions = Microsoft.Extensions.Options.Options;

namespace CodebaseIndexer.Application.Tests;

/// <summary>Unit tests for expand_search_context orchestration.</summary>
public sealed class ExpandSearchContextServiceTests
{
    [Test]
    public async Task Expand_collects_seed_chunk_ids_and_clamps_hops()
    {
        var graph = new NoOpGraphStore { Enabled = true };
        var search = ISearchService.Mock();
        search.SearchCodebaseAsync(Any(), Any(), Any(), Any(), Any(), Any(), Any(), Any(), Any())
            .Returns(Result<SearchCodebaseResponse>.Success(
                new SearchCodebaseResponse(
                [
                    new SearchCodebaseHit(
                        "proj/a.py:1", 0.9, "proj", "a.py", "handler", SymbolType.Function, 1, 10, SourceLanguage.Python, "def handler(): ...", null),
                ],
                ["proj"],
                [])));
        search.SearchSymbolsAsync(Any(), Any(), Any(), Any(), Any(), Any(), Any(), Any())
            .Returns(Result<SearchSymbolsResponse>.Success(new SearchSymbolsResponse([], ["proj"])));

        var service = CreateService(search, new NoOpVectorStore(), graph, maxHops: 2);

        await service.ExpandSearchContextAsync("find handler", collection: "proj", graphHops: 99);

        await Assert.That(graph.LastExpandChunkIds).IsEquivalentTo(["proj/a.py:1"]);
        await Assert.That(graph.LastExpandHops).IsEqualTo(2);
        await Assert.That(graph.LastExpandMaxNodes).IsEqualTo(200);
        search.SearchCodebaseAsync(Any(), Any(), Any(), Any(), Any(), Any(), Any(), Any(), Any())
            .WasCalled(Times.Once);
    }

    [Test]
    public async Task Expand_returns_empty_graph_when_disabled()
    {
        var graph = new NoOpGraphStore { Enabled = false };
        var search = ISearchService.Mock();
        search.SearchCodebaseAsync(Any(), Any(), Any(), Any(), Any(), Any(), Any(), Any(), Any())
            .Returns(Result<SearchCodebaseResponse>.Success(
                new SearchCodebaseResponse(
                [
                    new SearchCodebaseHit(
                        "c1", 0.5, "proj", "a.py", "f", SymbolType.Function, 1, 2, SourceLanguage.Python, "x", null),
                ],
                ["proj"],
                [])));
        search.SearchSymbolsAsync(Any(), Any(), Any(), Any(), Any(), Any(), Any(), Any())
            .Returns(Result<SearchSymbolsResponse>.Success(new SearchSymbolsResponse([], ["proj"])));

        var service = CreateService(search, new NoOpVectorStore(), graph);

        var result = await service.ExpandSearchContextAsync("q", collection: "proj");
        await Assert.That(result.IsSuccess).IsTrue();
        var json = System.Text.Json.JsonSerializer.Serialize(result.Value);
        await Assert.That(json).Contains("\"nodes\":[]");
        await Assert.That(json).Contains("\"edges\":[]");
        await Assert.That(graph.LastExpandChunkIds).IsNull();
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
}
