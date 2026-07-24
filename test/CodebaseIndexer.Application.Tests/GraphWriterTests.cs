using CodebaseIndexer.Application.Graph;
using CodebaseIndexer.Application.Search;
using CodebaseIndexer.Domain.Models;
using Microsoft.Extensions.Logging.Abstractions;
using System.Threading.Tasks;

namespace CodebaseIndexer.Application.Tests;

/// <summary>Unit tests for index-time graph writer (Python test_graph_writer parity).</summary>
public sealed class GraphWriterTests
{
    [Test]
    public async Task Symbol_qualified_name_format()
    {
        await Assert.That(GraphWriter.SymbolQualifiedName("coll", "src/a.py", "MyClass")).IsEqualTo("coll:src/a.py::MyClass");
    }

    [Test]
    public async Task Resolve_call_target_unifies_unique_method_name()
    {
        var defines = new Dictionary<string, List<GraphDefineEntry>>(StringComparer.Ordinal)
        {
            ["isEnabled"] =
            [
                new GraphDefineEntry("demo:Feature.java::isEnabled", "Feature.java", "isEnabled", SymbolType.Method),
            ],
        };

        var (qn, name) = GraphWriter.ResolveCallTarget("isEnabled", "demo", "Caller.java", defines, []);
        await Assert.That(qn).IsEqualTo("demo:Feature.java::isEnabled");
        await Assert.That(name).IsEqualTo("isEnabled");
    }

    [Test]
    public async Task Resolve_call_target_falls_back_to_callee_token()
    {
        var (qn, name) = GraphWriter.ResolveCallTarget(
            "featureService.isEnabled",
            "demo",
            "Caller.java",
            new Dictionary<string, List<GraphDefineEntry>>(),
            []);
        await Assert.That(qn).IsEqualTo("demo::callee::featureService.isEnabled");
        await Assert.That(name).IsEqualTo("featureService.isEnabled");
    }

    [Test]
    public async Task Graph_node_ids_from_batch_neighbor_keys_only()
    {
        var batch = new GraphBatch("demo");
        batch.Chunks.Add(new GraphChunkRow("c1", "a.py", 1, 5));
        batch.Chunks.Add(new GraphChunkRow("c2", "a.py", 6, 9));
        batch.Defines.Add(new GraphDefineRow("c1", "demo:a.py::foo", "foo", SymbolType.Function));
        batch.Calls.Add(new GraphCallRow("c1", "demo::callee::bar", "bar", "bar"));
        batch.Calls.Add(new GraphCallRow("c1", "demo::callee::bar", "bar", "bar"));
        batch.DeclaresEndpoint.Add(new GraphDeclaresEndpointRow("c1", "/api/users"));
        batch.HttpCalls.Add(new GraphHttpCallRow("c2", "/api/profile"));
        batch.Imports.Add(new GraphImportRow("a.py", "demo::import::os", "os"));

        var mapping = GraphWriter.GraphNodeIdsFromBatch(batch);

        await Assert.That(mapping["c1"]).IsEquivalentTo(["demo:a.py::foo", "demo::callee::bar", "demo:/api/users", "demo::import::os"]);
        await Assert.That(mapping["c2"]).IsEquivalentTo(["demo:/api/profile", "demo::import::os"]);
        foreach (var keys in mapping.Values)
        {
            await Assert.That(keys).DoesNotContain("c1");
            await Assert.That(keys).DoesNotContain("c2");
        }
    }

    [Test]
    public async Task Graph_node_ids_from_batch_empty()
    {
        await Assert.That(GraphWriter.GraphNodeIdsFromBatch(new GraphBatch("demo"))).IsEmpty();
    }

    [Test]
    public async Task Build_graph_batch_from_chunks()
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
                SourceLanguage.Java,
                "sha",
                SymbolType.Method)
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

        await Assert.That(batch.Collection).IsEqualTo("demo");
        await Assert.That(batch.Files).HasSingleItem();
        await Assert.That(batch.Chunks).HasSingleItem();
        await Assert.That(batch.Defines).Contains(d => d.Name == "getUsers");
        await Assert.That(batch.Calls).Contains(c => c.Name == "get" && c.CallToken == "get");
        await Assert.That(batch.DeclaresEndpoint.Count > 0 || batch.HttpCalls.Count > 0).IsTrue();
    }
}