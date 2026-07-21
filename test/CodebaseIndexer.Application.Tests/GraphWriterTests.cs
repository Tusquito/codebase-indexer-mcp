using CodebaseIndexer.Application.Graph;
using CodebaseIndexer.Application.Search;
using CodebaseIndexer.Domain.Models;
using Microsoft.Extensions.Logging.Abstractions;

namespace CodebaseIndexer.Application.Tests;

/// <summary>Unit tests for index-time graph writer (Python test_graph_writer parity).</summary>
public sealed class GraphWriterTests
{
    [Fact]
    public void Symbol_qualified_name_format()
    {
        Assert.Equal(
            "coll:src/a.py::MyClass",
            GraphWriter.SymbolQualifiedName("coll", "src/a.py", "MyClass"));
    }

    [Fact]
    public void Resolve_call_target_unifies_unique_method_name()
    {
        var defines = new Dictionary<string, List<GraphDefineEntry>>(StringComparer.Ordinal)
        {
            ["isEnabled"] =
            [
                new GraphDefineEntry("demo:Feature.java::isEnabled", "Feature.java", "isEnabled", "method"),
            ],
        };

        var (qn, name) = GraphWriter.ResolveCallTarget("isEnabled", "demo", "Caller.java", defines, []);
        Assert.Equal("demo:Feature.java::isEnabled", qn);
        Assert.Equal("isEnabled", name);
    }

    [Fact]
    public void Resolve_call_target_falls_back_to_callee_token()
    {
        var (qn, name) = GraphWriter.ResolveCallTarget(
            "featureService.isEnabled",
            "demo",
            "Caller.java",
            new Dictionary<string, List<GraphDefineEntry>>(),
            []);
        Assert.Equal("demo::callee::featureService.isEnabled", qn);
        Assert.Equal("featureService.isEnabled", name);
    }

    [Fact]
    public void Graph_node_ids_from_batch_neighbor_keys_only()
    {
        var batch = new GraphBatch("demo");
        batch.Chunks.Add(new GraphChunkRow("c1", "a.py", 1, 5));
        batch.Chunks.Add(new GraphChunkRow("c2", "a.py", 6, 9));
        batch.Defines.Add(new GraphDefineRow("c1", "demo:a.py::foo", "foo", "function"));
        batch.Calls.Add(new GraphCallRow("c1", "demo::callee::bar", "bar", "bar"));
        batch.Calls.Add(new GraphCallRow("c1", "demo::callee::bar", "bar", "bar"));
        batch.DeclaresEndpoint.Add(new GraphDeclaresEndpointRow("c1", "/api/users"));
        batch.HttpCalls.Add(new GraphHttpCallRow("c2", "/api/profile"));
        batch.Imports.Add(new GraphImportRow("a.py", "demo::import::os", "os"));

        var mapping = GraphWriter.GraphNodeIdsFromBatch(batch);

        Assert.Equal(
            ["demo:a.py::foo", "demo::callee::bar", "demo:/api/users", "demo::import::os"],
            mapping["c1"]);
        Assert.Equal(["demo:/api/profile", "demo::import::os"], mapping["c2"]);
        foreach (var keys in mapping.Values)
        {
            Assert.DoesNotContain("c1", keys);
            Assert.DoesNotContain("c2", keys);
        }
    }

    [Fact]
    public void Graph_node_ids_from_batch_empty()
    {
        Assert.Empty(GraphWriter.GraphNodeIdsFromBatch(new GraphBatch("demo")));
    }

    [Fact]
    public void Build_graph_batch_from_chunks()
    {
        var writer = new GraphWriter(NullLogger<GraphWriter>.Instance);
        var chunks = new[]
        {
            new Chunk(
                new ChunkId("c1"),
                "demo/src/UserController.java",
                "@GetMapping(\"/api/users\")\npublic void getUsers() { client.get(\"/api/profile\"); }",
                1,
                3,
                "getUsers",
                "java",
                "sha",
                "method")
            {
                Callees = ["get"],
            },
        };

        var batch = writer.BuildGraphBatch(
            "demo",
            chunks,
            new UrlExtractors(["api", "rest"]),
            workspacePath: Path.GetTempPath(),
            collectionNames: ["demo", "other"]);

        Assert.Equal("demo", batch.Collection);
        Assert.Single(batch.Files);
        Assert.Single(batch.Chunks);
        Assert.Contains(batch.Defines, d => d.Name == "getUsers");
        Assert.Contains(batch.Calls, c => c.Name == "get" && c.CallToken == "get");
        Assert.True(batch.DeclaresEndpoint.Count > 0 || batch.HttpCalls.Count > 0);
    }
}
