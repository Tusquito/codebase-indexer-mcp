using System.Diagnostics;
using System.Threading.Channels;
using CodebaseIndexer.Application.Options;
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
    private readonly IndexingOptions _options;
    private readonly WorkspaceOptions _workspace;
    private readonly ILogger<IndexCodebaseService> _logger;

    /// <summary>Creates the indexing pipeline service.</summary>
    /// <param name="scanner">Workspace file scanner.</param>
    /// <param name="chunker">Source code chunker.</param>
    /// <param name="embedder">Chunk embedding service.</param>
    /// <param name="vectorStore">Vector store for upserts and metadata.</param>
    /// <param name="options">Indexing pipeline options.</param>
    /// <param name="workspace">Workspace scan options.</param>
    /// <param name="logger">Logger instance.</param>
    public IndexCodebaseService(
        IWorkspaceScanner scanner,
        ICodeChunker chunker,
        IIndexEmbeddingService embedder,
        IVectorStore vectorStore,
        IOptions<IndexingOptions> options,
        IOptions<WorkspaceOptions> workspace,
        ILogger<IndexCodebaseService> logger)
    {
        _scanner = scanner;
        _chunker = chunker;
        _embedder = embedder;
        _vectorStore = vectorStore;
        _options = options.Value;
        _workspace = workspace.Value;
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
                flushChannel.Reader, collection, progress, errors, cancellationToken);

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
            }

            if (batch.Chunks.Count == 0)
            {
                continue;
            }

            try
            {
                var embedded = await _embedder.EmbedChunksAsync(batch.Chunks, cancellationToken).ConfigureAwait(false);
                progress.AddChunks(batch.Chunks.Count);
                inflightUpsert = UpsertBatchesAsync(collection, embedded, cancellationToken);
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
        CancellationToken cancellationToken)
    {
        for (var offset = 0; offset < embedded.Count; offset += _options.UpsertBatch)
        {
            var count = Math.Min(_options.UpsertBatch, embedded.Count - offset);
            var batch = new EmbeddedChunk[count];
            for (var i = 0; i < count; i++)
            {
                batch[i] = embedded[offset + i];
            }

            await _vectorStore.UpsertChunksAsync(collection, batch, cancellationToken).ConfigureAwait(false);
        }
    }
}
