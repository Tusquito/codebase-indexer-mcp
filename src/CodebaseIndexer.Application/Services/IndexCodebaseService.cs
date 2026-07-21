using System.Diagnostics;
using System.Threading.Channels;
using CodebaseIndexer.Application.Graph;
using CodebaseIndexer.Application.Options;
using CodebaseIndexer.Application.Search;
using CodebaseIndexer.Domain.Exceptions;
using CodebaseIndexer.Domain.Models;
using CodebaseIndexer.Domain.Ports;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CodebaseIndexer.Application.Services;

/// <summary>Scans, chunks, embeds, and upserts code into a vector collection.</summary>
public sealed class IndexCodebaseService : IIndexCodebaseService, IIndexPipeline
{
    private sealed record ChunkFlushBatch(IReadOnlyList<Chunk> Chunks, IReadOnlyList<string> ModifiedPaths);

    private sealed class PipelineProgress
    {
        public int TotalFiles { get; private set; }
        public int IndexedFiles { get; private set; }
        public int SkippedFiles { get; private set; }
        public int TotalChunks { get; private set; }

        public void RecordScannedFile(bool indexed, bool skipped)
        {
            TotalFiles++;
            if (skipped)
            {
                SkippedFiles++;
                return;
            }

            if (indexed)
            {
                IndexedFiles++;
            }
        }

        public void AddChunks(int count) => TotalChunks += count;
    }

    private readonly IWorkspaceScanner _scanner;
    private readonly ICodeChunker _chunker;
    private readonly IIndexEmbeddingService _embedder;
    private readonly IVectorStore _vectorStore;
    private readonly IGraphStore _graphStore;
    private readonly GraphWriter _graphWriter;
    private readonly UrlExtractors _urlExtractors;
    private readonly IndexingOptions _options;
    private readonly WorkspaceOptions _workspace;
    private readonly GraphOptions _graphOptions;
    private readonly ILogger<IndexCodebaseService> _logger;

    /// <summary>Creates the indexing pipeline service.</summary>
    public IndexCodebaseService(
        IWorkspaceScanner scanner,
        ICodeChunker chunker,
        IIndexEmbeddingService embedder,
        IVectorStore vectorStore,
        IGraphStore graphStore,
        GraphWriter graphWriter,
        UrlExtractors urlExtractors,
        IOptions<IndexingOptions> options,
        IOptions<WorkspaceOptions> workspace,
        IOptions<GraphOptions> graphOptions,
        ILogger<IndexCodebaseService> logger)
    {
        _scanner = scanner;
        _chunker = chunker;
        _embedder = embedder;
        _vectorStore = vectorStore;
        _graphStore = graphStore;
        _graphWriter = graphWriter;
        _urlExtractors = urlExtractors;
        _options = options.Value;
        _workspace = workspace.Value;
        _graphOptions = graphOptions.Value;
        _logger = logger;
    }

    /// <inheritdoc />
    public Task<PipelineResult> RunAsync(
        string collection,
        string subPath,
        bool force,
        CancellationToken cancellationToken) =>
        RunPipelineAsync(collection, subPath, force, cancellationToken);

    private async Task<PipelineResult> RunPipelineAsync(
        string collection,
        string subPath,
        bool force,
        CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        var progress = new PipelineProgress();
        var errors = new List<string>();

        await _vectorStore.EnsureCollectionAsync(collection, force, cancellationToken).ConfigureAwait(false);
        var existingMetadata = await _vectorStore.GetFileMetadataAsync(collection, cancellationToken).ConfigureAwait(false);
        var existingHashes = existingMetadata.ToDictionary(kv => kv.Key, kv => kv.Value.Sha256, StringComparer.Ordinal);

        var graphActive = _graphOptions.Enabled
            && await _graphStore.IsEnabledAsync(cancellationToken).ConfigureAwait(false);
        IReadOnlyList<string> collectionNames = [collection];
        if (graphActive)
        {
            try
            {
                await _graphStore.EnsureSchemaAsync(cancellationToken).ConfigureAwait(false);
                var stats = await _vectorStore.ListCollectionStatsAsync(cancellationToken).ConfigureAwait(false);
                var names = stats.Select(s => s.Name).ToList();
                if (!names.Contains(collection, StringComparer.Ordinal))
                {
                    names.Add(collection);
                }

                collectionNames = names;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "graph_schema_init_error");
                errors.Add($"Graph schema init error: {ex.Message}");
                graphActive = false;
            }
        }

        var scannedPaths = new HashSet<string>(StringComparer.Ordinal);
        var indexingPaused = false;

        try
        {
            await _vectorStore.SetIndexingAsync(collection, enabled: false, cancellationToken).ConfigureAwait(false);
            indexingPaused = true;

            var flushChannel = Channel.CreateBounded<ChunkFlushBatch>(new BoundedChannelOptions(2)
            {
                FullMode = BoundedChannelFullMode.Wait,
                SingleWriter = true,
            });

            var parseTask = ParseFilesAsync(
                collection, subPath, force, existingMetadata, existingHashes, scannedPaths,
                progress, flushChannel.Writer, cancellationToken);
            var embedUpsertTask = EmbedAndUpsertAsync(
                flushChannel.Reader, collection, progress, errors, graphActive, collectionNames, cancellationToken);

            await Task.WhenAll(parseTask, embedUpsertTask).ConfigureAwait(false);

            var stalePaths = new List<string>();
            foreach (var path in existingHashes.Keys)
            {
                if (!scannedPaths.Contains(path))
                {
                    stalePaths.Add(path);
                }
            }

            if (stalePaths.Count > 0)
            {
                await _vectorStore.DeleteByPathsAsync(collection, stalePaths, cancellationToken).ConfigureAwait(false);
                if (graphActive)
                {
                    try
                    {
                        await _graphStore.DeleteFilesAsync(collection, stalePaths, cancellationToken)
                            .ConfigureAwait(false);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "graph_stale_delete_error");
                        errors.Add($"Graph stale delete error: {ex.Message}");
                    }
                }
            }
        }
        catch (IndexCancelledException ex)
        {
            errors.Add(ex.Message);
            throw;
        }
        finally
        {
            if (indexingPaused)
            {
                await _vectorStore.SetIndexingAsync(collection, enabled: true, cancellationToken).ConfigureAwait(false);
            }

            if (_options.ReleaseModelsAfterIndex)
            {
                _embedder.ReleaseModels();
            }
        }

        if (graphActive)
        {
            try
            {
                await _vectorStore.SetCollectionGraphCallSitesAsync(collection, enabled: true, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "graph_call_sites_metadata_error collection={Collection}", collection);
                errors.Add($"Graph call-sites metadata error: {ex.Message}");
            }

            try
            {
                await _vectorStore.SetCollectionGraphEnabledAsync(collection, enabled: true, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "graph_enabled_metadata_error collection={Collection}", collection);
                errors.Add($"Graph enabled metadata error: {ex.Message}");
            }
        }

        stopwatch.Stop();
        return new PipelineResult(
            progress.TotalFiles,
            progress.IndexedFiles,
            progress.SkippedFiles,
            progress.TotalChunks,
            Math.Round(stopwatch.Elapsed.TotalSeconds, 2),
            errors);
    }

    private async Task ParseFilesAsync(
        string collection,
        string subPath,
        bool force,
        IReadOnlyDictionary<string, FileMetadata> existingMetadata,
        Dictionary<string, string> existingHashes,
        HashSet<string> scannedPaths,
        PipelineProgress progress,
        ChannelWriter<ChunkFlushBatch> flushWriter,
        CancellationToken cancellationToken)
    {
        _ = collection;
        var pending = new List<Chunk>();
        var pendingModifiedPaths = new List<string>();
        try
        {
            await foreach (var file in _scanner.ScanFilesAsync(
                _workspace.Path,
                subPath,
                existingMetadata,
                force,
                cancellationToken).ConfigureAwait(false))
            {
                cancellationToken.ThrowIfCancellationRequested();
                scannedPaths.Add(file.RelPath);

                if (file.MtimeSkipped)
                {
                    progress.RecordScannedFile(indexed: false, skipped: true);
                    continue;
                }

                if (!force && existingHashes.TryGetValue(file.RelPath, out var existingHash)
                    && existingHash == file.Sha256Hash)
                {
                    progress.RecordScannedFile(indexed: false, skipped: true);
                    continue;
                }

                progress.RecordScannedFile(indexed: true, skipped: false);
                if (existingHashes.ContainsKey(file.RelPath))
                {
                    pendingModifiedPaths.Add(file.RelPath);
                }

                try
                {
                    pending.AddRange(_chunker.ChunkFile(file.RelPath, file.Content, file.Language, file.Sha256Hash));
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "indexing_error path={Path}", file.RelPath);
                }

                if (pending.Count >= _options.FlushEvery)
                {
                    await flushWriter.WriteAsync(
                        new ChunkFlushBatch(pending.ToArray(), pendingModifiedPaths.ToArray()),
                        cancellationToken).ConfigureAwait(false);
                    pending.Clear();
                    pendingModifiedPaths.Clear();
                }
            }

            if (pending.Count > 0 || pendingModifiedPaths.Count > 0)
            {
                await flushWriter.WriteAsync(
                    new ChunkFlushBatch(pending.ToArray(), pendingModifiedPaths.ToArray()),
                    cancellationToken).ConfigureAwait(false);
            }
        }
        finally
        {
            flushWriter.TryComplete();
        }
    }

    private async Task EmbedAndUpsertAsync(
        ChannelReader<ChunkFlushBatch> reader,
        string collection,
        PipelineProgress progress,
        List<string> errors,
        bool graphActive,
        IReadOnlyList<string> collectionNames,
        CancellationToken cancellationToken)
    {
        Task? inflightUpsert = null;
        await foreach (var batch in reader.ReadAllAsync(cancellationToken).ConfigureAwait(false))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (inflightUpsert is not null)
            {
                try
                {
                    await inflightUpsert.ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    errors.Add($"Upsert error: {ex.Message}");
                }
            }

            if (batch.ModifiedPaths.Count > 0)
            {
                await _vectorStore.DeleteByPathsAsync(collection, batch.ModifiedPaths, cancellationToken)
                    .ConfigureAwait(false);
                if (graphActive)
                {
                    try
                    {
                        await _graphStore.DeleteFilesAsync(collection, batch.ModifiedPaths, cancellationToken)
                            .ConfigureAwait(false);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "graph_delete_error");
                        errors.Add($"Graph delete error: {ex.Message}");
                    }
                }
            }

            if (batch.Chunks.Count == 0)
            {
                continue;
            }

            try
            {
                var embedded = await _embedder.EmbedChunksAsync(batch.Chunks, cancellationToken).ConfigureAwait(false);
                progress.AddChunks(batch.Chunks.Count);
                inflightUpsert = UpsertBatchesAsync(
                    collection, embedded, graphActive, collectionNames, errors, cancellationToken);
            }
            catch (Exception ex)
            {
                errors.Add($"Batch embed/upsert error: {ex.Message}");
            }
        }

        if (inflightUpsert is not null)
        {
            await inflightUpsert.ConfigureAwait(false);
        }
    }

    private async Task UpsertBatchesAsync(
        string collection,
        IReadOnlyList<EmbeddedChunk> embedded,
        bool graphActive,
        IReadOnlyList<string> collectionNames,
        List<string> errors,
        CancellationToken cancellationToken)
    {
        GraphBatch? graphBatch = null;
        IReadOnlyDictionary<string, IReadOnlyList<string>>? graphNodeIdsByChunk = null;
        if (graphActive)
        {
            try
            {
                var chunks = embedded.Select(e => e.Chunk).ToArray();
                graphBatch = _graphWriter.BuildGraphBatch(
                    collection,
                    chunks,
                    _urlExtractors,
                    _workspace.Path,
                    collectionNames);
                graphNodeIdsByChunk = GraphWriter.GraphNodeIdsFromBatch(graphBatch);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "graph_batch_build_error");
                errors.Add($"Graph batch build error: {ex.Message}");
            }
        }

        for (var offset = 0; offset < embedded.Count; offset += _options.UpsertBatch)
        {
            var count = Math.Min(_options.UpsertBatch, embedded.Count - offset);
            var batch = new EmbeddedChunk[count];
            for (var i = 0; i < count; i++)
            {
                batch[i] = embedded[offset + i];
            }

            await _vectorStore.UpsertChunksAsync(
                collection,
                batch,
                // Only strip Qdrant callees when the graph batch was built successfully;
                // a failed build must not leave Neo4j empty while callees are omitted.
                omitCallees: graphBatch is not null,
                graphNodeIdsByChunk: graphNodeIdsByChunk,
                cancellationToken).ConfigureAwait(false);
        }

        if (graphActive && graphBatch is not null)
        {
            try
            {
                await _graphStore.WriteBatchAsync(graphBatch, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "graph_write_error");
                errors.Add($"Graph write error: {ex.Message}");
            }
        }
    }
}
