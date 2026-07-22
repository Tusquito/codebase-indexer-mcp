using CodebaseIndexer.Application.Graph;
using CodebaseIndexer.Application.Options;
using CodebaseIndexer.Application.Search;
using CodebaseIndexer.Application.Services;
using CodebaseIndexer.Domain.Models;
using CodebaseIndexer.Domain.Ports;
using Microsoft.Extensions.Logging.Abstractions;
using MsOptions = Microsoft.Extensions.Options.Options;

namespace CodebaseIndexer.Application.Tests;

/// <summary>Unit tests for index-time graph hooks (Python <c>test_pipeline_graph</c> parity).</summary>
public sealed class IndexCodebaseServiceTests
{
    [Fact]
    public async Task Run_graph_disabled_skips_schema()
    {
        var graph = new NoOpGraphStore { Enabled = false };
        var vector = new RecordingVectorStore();
        var service = CreateService(graph, vector, graphOptionsEnabled: false);

        var result = await service.RunAsync("demo", subPath: "", force: true, CancellationToken.None);

        Assert.Empty(graph.EnsuredSchemaCalls);
        Assert.Empty(graph.WrittenBatches);
        Assert.Empty(result.Errors);
        Assert.Empty(vector.UpsertCalls);
    }

    [Fact]
    public async Task Run_graph_schema_error_appended_and_skips_graph_io()
    {
        var graph = new NoOpGraphStore
        {
            Enabled = true,
            EnsureSchemaException = new InvalidOperationException("neo4j down"),
        };
        var vector = new RecordingVectorStore();
        var chunk = SampleChunk();
        var service = CreateService(
            graph,
            vector,
            files: [SampleFile()],
            chunksByPath: new Dictionary<string, IReadOnlyList<Chunk>>(StringComparer.Ordinal)
            {
                ["a.py"] = [chunk],
            });

        var result = await service.RunAsync("demo", subPath: "", force: true, CancellationToken.None);

        Assert.Single(graph.EnsuredSchemaCalls);
        Assert.Empty(graph.WrittenBatches);
        Assert.Contains(result.Errors, e => e.Contains("Graph schema init error", StringComparison.Ordinal));
        Assert.Single(vector.UpsertCalls);
        Assert.False(vector.UpsertCalls[0].OmitCallees);
        Assert.Null(vector.UpsertCalls[0].GraphNodeIdsByChunk);
        Assert.Empty(vector.GraphCallSitesCollections);
        Assert.Empty(vector.GraphEnabledCollections);
    }

    [Fact]
    public async Task Run_graph_success_ensures_schema_writes_batch_omits_callees_stamps_metadata()
    {
        var graph = new NoOpGraphStore { Enabled = true };
        var vector = new RecordingVectorStore();
        var chunk = SampleChunk(callees: ["helper"]);
        var service = CreateService(
            graph,
            vector,
            files: [SampleFile()],
            chunksByPath: new Dictionary<string, IReadOnlyList<Chunk>>(StringComparer.Ordinal)
            {
                ["a.py"] = [chunk],
            });

        var result = await service.RunAsync("demo", subPath: "", force: true, CancellationToken.None);

        Assert.Empty(result.Errors);
        Assert.Single(graph.EnsuredSchemaCalls);
        Assert.Single(graph.WrittenBatches);
        Assert.Equal("demo", graph.WrittenBatches[0].Collection);
        Assert.Single(vector.UpsertCalls);
        Assert.True(vector.UpsertCalls[0].OmitCallees);
        Assert.NotNull(vector.UpsertCalls[0].GraphNodeIdsByChunk);
        Assert.Equal(["demo"], vector.GraphCallSitesCollections);
        Assert.Equal(["demo"], vector.GraphEnabledCollections);
    }

    [Fact]
    public async Task Run_graph_batch_build_failure_does_not_omit_callees()
    {
        var graph = new NoOpGraphStore { Enabled = true };
        var vector = new RecordingVectorStore();
        // Force GraphWriter.BuildGraphBatch to throw (foreach over null Callees).
        var chunk = SampleChunkWithNullCallees();
        var service = CreateService(
            graph,
            vector,
            files: [SampleFile()],
            chunksByPath: new Dictionary<string, IReadOnlyList<Chunk>>(StringComparer.Ordinal)
            {
                ["a.py"] = [chunk],
            });

        var result = await service.RunAsync("demo", subPath: "", force: true, CancellationToken.None);

        Assert.Contains(result.Errors, e => e.Contains("Graph batch build error", StringComparison.Ordinal));
        Assert.Empty(graph.WrittenBatches);
        Assert.Single(vector.UpsertCalls);
        Assert.False(vector.UpsertCalls[0].OmitCallees);
        Assert.Null(vector.UpsertCalls[0].GraphNodeIdsByChunk);
    }

    [Fact]
    public async Task Run_graph_deletes_stale_paths_from_graph_store()
    {
        var graph = new NoOpGraphStore { Enabled = true };
        var vector = new RecordingVectorStore
        {
            ExistingMetadata =
            {
                ["gone.py"] = new FileMetadata("old-sha", 1),
            },
        };
        var service = CreateService(graph, vector, files: []);

        await service.RunAsync("demo", subPath: "", force: true, CancellationToken.None);

        Assert.Single(graph.DeleteCalls);
        Assert.Equal("demo", graph.DeleteCalls[0].Collection);
        Assert.Equal(["gone.py"], graph.DeleteCalls[0].Paths);
        Assert.Equal(["demo"], vector.GraphCallSitesCollections);
        Assert.Equal(["demo"], vector.GraphEnabledCollections);
    }

    [Fact]
    public async Task Run_graph_deletes_modified_paths_before_upsert()
    {
        var graph = new NoOpGraphStore { Enabled = true };
        var vector = new RecordingVectorStore
        {
            ExistingMetadata =
            {
                ["a.py"] = new FileMetadata("old-sha", 1),
            },
        };
        var chunk = SampleChunk();
        var service = CreateService(
            graph,
            vector,
            files: [SampleFile(sha: "new-sha")],
            chunksByPath: new Dictionary<string, IReadOnlyList<Chunk>>(StringComparer.Ordinal)
            {
                ["a.py"] = [chunk],
            });

        await service.RunAsync("demo", subPath: "", force: false, CancellationToken.None);

        Assert.Contains(graph.DeleteCalls, d => d.Paths.Contains("a.py"));
        Assert.Single(graph.WrittenBatches);
        Assert.True(vector.UpsertCalls[0].OmitCallees);
    }

    private static IndexCodebaseService CreateService(
        NoOpGraphStore graph,
        RecordingVectorStore vector,
        IReadOnlyList<FileRecord>? files = null,
        IReadOnlyDictionary<string, IReadOnlyList<Chunk>>? chunksByPath = null,
        bool graphOptionsEnabled = true)
    {
        files ??= [];
        chunksByPath ??= new Dictionary<string, IReadOnlyList<Chunk>>(StringComparer.Ordinal);
        var scanner = new StubScanner(files);
        var chunker = new StubChunker(chunksByPath);
        var embedder = new StubEmbedder();
        return new IndexCodebaseService(
            scanner,
            chunker,
            embedder,
            vector,
            graph,
            new GraphWriter(NullLogger<GraphWriter>.Instance),
            new UrlExtractors([]),
            MsOptions.Create(new IndexingOptions
            {
                FlushEvery = 10_000,
                UpsertBatch = 64,
                ReleaseModelsAfterIndex = false,
            }),
            MsOptions.Create(new WorkspaceOptions { Path = "/tmp/workspace" }),
            MsOptions.Create(new GraphOptions
            {
                Enabled = graphOptionsEnabled,
                Neo4jUri = "bolt://localhost:7687",
                Neo4jUser = "neo4j",
                Neo4jPassword = "pw",
                Neo4jDatabase = "neo4j",
                WriterBatch = 500,
                MaxHops = 2,
                MaxNodes = 200,
            }),
            NullLogger<IndexCodebaseService>.Instance);
    }

    private static FileRecord SampleFile(string sha = "sha") =>
        new("/tmp/workspace/a.py", "a.py", SourceLanguage.Python, "def hello(): pass", sha);

    private static Chunk SampleChunk(IReadOnlyList<string>? callees = null) =>
        new(
            new ChunkId("c1"),
            "a.py",
            "def hello(): pass",
            1,
            1,
            "hello",
            SourceLanguage.Python,
            "sha",
            SymbolType.Function)
        {
            Callees = callees ?? [],
        };

    private static Chunk SampleChunkWithNullCallees() =>
        new(
            new ChunkId("c1"),
            "a.py",
            "def hello(): pass",
            1,
            1,
            "hello",
            SourceLanguage.Python,
            "sha",
            SymbolType.Function)
        {
            Callees = null!,
        };

    private sealed class StubScanner(IReadOnlyList<FileRecord> files) : IWorkspaceScanner
    {
        public async IAsyncEnumerable<FileRecord> ScanFilesAsync(
            string workspacePath,
            string subPath,
            IReadOnlyDictionary<string, FileMetadata>? existingMetadata,
            bool force,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            _ = workspacePath;
            _ = subPath;
            _ = existingMetadata;
            _ = force;
            foreach (var file in files)
            {
                cancellationToken.ThrowIfCancellationRequested();
                yield return file;
                await Task.Yield();
            }
        }
    }

    private sealed class StubChunker(IReadOnlyDictionary<string, IReadOnlyList<Chunk>> chunksByPath) : ICodeChunker
    {
        public IReadOnlyList<Chunk> ChunkFile(string relPath, string content, SourceLanguage language, string fileSha256)
        {
            _ = content;
            _ = language;
            _ = fileSha256;
            return chunksByPath.TryGetValue(relPath, out var chunks) ? chunks : [];
        }
    }

    private sealed class StubEmbedder : IIndexEmbeddingService
    {
        public Task PreloadAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        public void ReleaseModels()
        {
        }

        public Task<IReadOnlyList<EmbeddedChunk>> EmbedChunksAsync(
            IReadOnlyList<Chunk> chunks,
            CancellationToken cancellationToken = default)
        {
            IReadOnlyList<EmbeddedChunk> embedded = chunks
                .Select(c => new EmbeddedChunk(c, [0.1f, 0.2f], null))
                .ToArray();
            return Task.FromResult(embedded);
        }
    }

    private sealed class RecordingVectorStore : NoOpVectorStore
    {
        public Dictionary<string, FileMetadata> ExistingMetadata { get; } = new(StringComparer.Ordinal);

        public List<(
            string Collection,
            IReadOnlyList<EmbeddedChunk> Chunks,
            bool OmitCallees,
            IReadOnlyDictionary<string, IReadOnlyList<string>>? GraphNodeIdsByChunk)> UpsertCalls
        { get; } = [];

        public List<string> GraphCallSitesCollections { get; } = [];

        public List<string> GraphEnabledCollections { get; } = [];

        public List<(string Collection, IReadOnlyList<string> Paths)> DeletedPaths { get; } = [];

        public override Task<IReadOnlyDictionary<string, FileMetadata>> GetFileMetadataAsync(
            string collection,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyDictionary<string, FileMetadata>>(ExistingMetadata);

        public override Task UpsertChunksAsync(
            string collection,
            IReadOnlyList<EmbeddedChunk> chunks,
            bool omitCallees = false,
            IReadOnlyDictionary<string, IReadOnlyList<string>>? graphNodeIdsByChunk = null,
            CancellationToken cancellationToken = default)
        {
            UpsertCalls.Add((collection, chunks, omitCallees, graphNodeIdsByChunk));
            return Task.CompletedTask;
        }

        public override Task SetCollectionGraphCallSitesAsync(
            string collection,
            bool enabled = true,
            CancellationToken cancellationToken = default)
        {
            GraphCallSitesCollections.Add(collection);
            return Task.CompletedTask;
        }

        public override Task SetCollectionGraphEnabledAsync(
            string collection,
            bool enabled = true,
            CancellationToken cancellationToken = default)
        {
            GraphEnabledCollections.Add(collection);
            return Task.CompletedTask;
        }

        public override Task DeleteByPathsAsync(
            string collection,
            IReadOnlyList<string> relPaths,
            CancellationToken cancellationToken = default)
        {
            DeletedPaths.Add((collection, relPaths));
            return Task.CompletedTask;
        }
    }
}
