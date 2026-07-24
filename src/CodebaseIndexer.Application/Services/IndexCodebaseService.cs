using System.Diagnostics;
using System.Threading.Channels;
using CodebaseIndexer.Application.Graph;
using CodebaseIndexer.Application.Options;
using CodebaseIndexer.Application.Search;
using CodebaseIndexer.Domain.Models;
using CodebaseIndexer.Domain.Ports;
using CodebaseIndexer.Domain.Results;
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
    public Task<Result<PipelineResult>> RunAsync(
        string collection,
        string subPath,
        bool force,
        CancellationToken cancellationToken) =>
        RunPipelineAsync(collection, subPath, force, cancellationToken);

    private async Task<Result<PipelineResult>> RunPipelineAsync(
        string collection,
        string subPath,
        bool force,
        CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        var progress = new PipelineProgress();
        var errors = new List<Error>();

        var ensureResult = await _vectorStore.EnsureCollectionAsync(collection, force, cancellationToken).ConfigureAwait(false);
        if (!ensureResult.IsSuccess)
        {
            return Result<PipelineResult>.Failure(ensureResult.Error);
        }

        var metadataResult = await _vectorStore.GetFileMetadataAsync(collection, cancellationToken).ConfigureAwait(false);
        if (!metadataResult.IsSuccess)
        {
            return Result<PipelineResult>.Failure(metadataResult.Error);
        }

        var existingMetadata = metadataResult.Value;
        var existingHashes = existingMetadata.ToDictionary(kv => kv.Key, kv => kv.Value.Sha256, StringComparer.Ordinal);

        var graphActive = _graphOptions.Enabled
            && await _graphStore.IsEnabledAsync(cancellationToken).ConfigureAwait(false);
        IReadOnlyList<string> collectionNames = [collection];
        if (graphActive)
        {
            var schemaResult = await _graphStore.EnsureSchemaAsync(cancellationToken).ConfigureAwait(false);
            if (!schemaResult.IsSuccess)
            {
                _logger.LogError("graph_schema_init_error code={Code} message={Message}", schemaResult.Error.Code, schemaResult.Error.Message);
                errors.Add(schemaResult.Error);
                graphActive = false;
            }
            else
            {
                var statsResult = await _vectorStore.ListCollectionStatsAsync(cancellationToken).ConfigureAwait(false);
                if (!statsResult.IsSuccess)
                {
                    errors.Add(statsResult.Error);
                    graphActive = false;
                }
                else
                {
                    var names = statsResult.Value.Select(s => s.Name).ToList();
                    if (!names.Contains(collection, StringComparer.Ordinal))
                    {
                        names.Add(collection);
                    }

                    collectionNames = names;
                }
            }
        }

        var scannedPaths = new HashSet<string>(StringComparer.Ordinal);
        var indexingPaused = false;

        try
        {
            var pauseResult = await _vectorStore.SetIndexingAsync(collection, enabled: false, cancellationToken).ConfigureAwait(false);
            if (!pauseResult.IsSuccess)
            {
                errors.Add(pauseResult.Error);
            }
            else
            {
                indexingPaused = true;
            }

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
                var deleteStale = await _vectorStore.DeleteByPathsAsync(collection, stalePaths, cancellationToken).ConfigureAwait(false);
                if (!deleteStale.IsSuccess)
                {
                    errors.Add(deleteStale.Error);
                }
                else if (graphActive)
                {
                    var graphDelete = await _graphStore.DeleteFilesAsync(collection, stalePaths, cancellationToken)
                        .ConfigureAwait(false);
                    if (!graphDelete.IsSuccess)
                    {
                        _logger.LogError("graph_stale_delete_error code={Code} message={Message}", graphDelete.Error.Code, graphDelete.Error.Message);
                        errors.Add(new Error(graphDelete.Error.Kind, IndexErrorCodes.GraphStaleDelete, graphDelete.Error.Message, graphDelete.Error.Metadata));
                    }
                }
            }
        }
        finally
        {
            if (indexingPaused)
            {
                var resumeResult = await _vectorStore.SetIndexingAsync(collection, enabled: true, cancellationToken).ConfigureAwait(false);
                if (!resumeResult.IsSuccess)
                {
                    errors.Add(resumeResult.Error);
                }
            }

            if (_options.ReleaseModelsAfterIndex)
            {
                _embedder.ReleaseModels();
            }
        }

        if (graphActive)
        {
            var callSitesResult = await _vectorStore.SetCollectionGraphCallSitesAsync(collection, enabled: true, cancellationToken)
                .ConfigureAwait(false);
            if (!callSitesResult.IsSuccess)
            {
                _logger.LogWarning("graph_call_sites_metadata_error collection={Collection} message={Message}", collection, callSitesResult.Error.Message);
                errors.Add(new Error(
                    ErrorKind.Dependency,
                    IndexErrorCodes.GraphCallSitesMetadata,
                    $"Graph call-sites metadata error: {callSitesResult.Error.Message}"));
            }

            var graphEnabledResult = await _vectorStore.SetCollectionGraphEnabledAsync(collection, enabled: true, cancellationToken)
                .ConfigureAwait(false);
            if (!graphEnabledResult.IsSuccess)
            {
                _logger.LogWarning("graph_enabled_metadata_error collection={Collection} message={Message}", collection, graphEnabledResult.Error.Message);
                errors.Add(new Error(
                    ErrorKind.Dependency,
                    IndexErrorCodes.GraphEnabledMetadata,
                    $"Graph enabled metadata error: {graphEnabledResult.Error.Message}"));
            }
        }

        stopwatch.Stop();
        return Result<PipelineResult>.Success(new PipelineResult(
            progress.TotalFiles,
            progress.IndexedFiles,
            progress.SkippedFiles,
            progress.TotalChunks,
            Math.Round(stopwatch.Elapsed.TotalSeconds, 2),
            errors));
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
        List<Error> errors,
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
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    errors.Add(new Error(
                        ErrorKind.Dependency,
                        IndexErrorCodes.Upsert,
                        $"Upsert error: {ex.Message}"));
                }
            }

            if (batch.ModifiedPaths.Count > 0)
            {
                var deleteModified = await _vectorStore.DeleteByPathsAsync(collection, batch.ModifiedPaths, cancellationToken)
                    .ConfigureAwait(false);
                if (!deleteModified.IsSuccess)
                {
                    errors.Add(deleteModified.Error);
                }
                else if (graphActive)
                {
                    var graphDelete = await _graphStore.DeleteFilesAsync(collection, batch.ModifiedPaths, cancellationToken)
                        .ConfigureAwait(false);
                    if (!graphDelete.IsSuccess)
                    {
                        _logger.LogError("graph_delete_error code={Code} message={Message}", graphDelete.Error.Code, graphDelete.Error.Message);
                        errors.Add(new Error(graphDelete.Error.Kind, IndexErrorCodes.GraphDelete, graphDelete.Error.Message, graphDelete.Error.Metadata));
                    }
                }
            }

            if (batch.Chunks.Count == 0)
            {
                continue;
            }

            var embedResult = await _embedder.EmbedChunksAsync(batch.Chunks, cancellationToken).ConfigureAwait(false);
            if (!embedResult.IsSuccess)
            {
                errors.Add(embedResult.Error);
                continue;
            }

            progress.AddChunks(batch.Chunks.Count);
            inflightUpsert = UpsertBatchesAsync(
                collection, embedResult.Value, graphActive, collectionNames, errors, cancellationToken);
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
        List<Error> errors,
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
                errors.Add(new Error(
                    ErrorKind.Dependency,
                    IndexErrorCodes.GraphBatchBuild,
                    $"Graph batch build error: {ex.Message}"));
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

            var upsertResult = await _vectorStore.UpsertChunksAsync(
                collection,
                batch,
                // Only strip Qdrant callees when the graph batch was built successfully;
                // a failed build must not leave Neo4j empty while callees are omitted.
                omitCallees: graphBatch is not null,
                graphNodeIdsByChunk: graphNodeIdsByChunk,
                cancellationToken).ConfigureAwait(false);
            if (!upsertResult.IsSuccess)
            {
                errors.Add(upsertResult.Error);
            }
        }

        if (graphActive && graphBatch is not null)
        {
            var writeResult = await _graphStore.WriteBatchAsync(graphBatch, cancellationToken).ConfigureAwait(false);
            if (!writeResult.IsSuccess)
            {
                _logger.LogError("graph_write_error code={Code} message={Message}", writeResult.Error.Code, writeResult.Error.Message);
                errors.Add(writeResult.Error);
            }
        }
    }
}
