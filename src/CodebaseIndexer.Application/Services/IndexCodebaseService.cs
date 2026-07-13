using System.Diagnostics;
using System.Threading.Channels;
using CodebaseIndexer.Application.Options;
using CodebaseIndexer.Domain.Exceptions;
using CodebaseIndexer.Domain.Models;
using CodebaseIndexer.Domain.Ports;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CodebaseIndexer.Application.Services;

public interface IIndexCodebaseService : IIndexPipeline;

public sealed class IndexCodebaseService : IIndexCodebaseService, IIndexPipeline
{
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
    private readonly ILogger<IndexCodebaseService> _logger;

    public IndexCodebaseService(
        IWorkspaceScanner scanner,
        ICodeChunker chunker,
        IIndexEmbeddingService embedder,
        IVectorStore vectorStore,
        IOptions<IndexingOptions> options,
        ILogger<IndexCodebaseService> logger)
    {
        _scanner = scanner;
        _chunker = chunker;
        _embedder = embedder;
        _vectorStore = vectorStore;
        _options = options.Value;
        _logger = logger;
    }

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
        var modifiedPaths = new List<string>();
        var indexingPaused = false;

        try
        {
            await _vectorStore.SetIndexingAsync(collection, enabled: false, cancellationToken).ConfigureAwait(false);
            indexingPaused = true;

            var flushChannel = Channel.CreateBounded<IReadOnlyList<Chunk>>(new BoundedChannelOptions(2)
            {
                FullMode = BoundedChannelFullMode.Wait,
                SingleWriter = true,
            });

            var parseTask = ParseFilesAsync(
                collection, subPath, force, existingMetadata, existingHashes, scannedPaths,
                modifiedPaths, progress, flushChannel.Writer, cancellationToken);
            var embedUpsertTask = EmbedAndUpsertAsync(
                flushChannel.Reader, collection, modifiedPaths, progress, errors, cancellationToken);

            await Task.WhenAll(parseTask, embedUpsertTask).ConfigureAwait(false);

            var stalePaths = existingHashes.Keys.Where(path => !scannedPaths.Contains(path)).ToArray();
            if (stalePaths.Length > 0)
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
        List<string> modifiedPaths,
        PipelineProgress progress,
        ChannelWriter<IReadOnlyList<Chunk>> flushWriter,
        CancellationToken cancellationToken)
    {
        var pending = new List<Chunk>();
        try
        {
            await foreach (var file in _scanner.ScanFilesAsync(
                _options.WorkspacePath,
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
                    modifiedPaths.Add(file.RelPath);
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
                    await flushWriter.WriteAsync(pending.ToArray(), cancellationToken).ConfigureAwait(false);
                    pending.Clear();
                }
            }

            if (pending.Count > 0)
            {
                await flushWriter.WriteAsync(pending.ToArray(), cancellationToken).ConfigureAwait(false);
            }
        }
        finally
        {
            flushWriter.TryComplete();
        }
    }

    private async Task EmbedAndUpsertAsync(
        ChannelReader<IReadOnlyList<Chunk>> reader,
        string collection,
        List<string> modifiedPaths,
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

            if (modifiedPaths.Count > 0)
            {
                await _vectorStore.DeleteByPathsAsync(collection, modifiedPaths.ToArray(), cancellationToken)
                    .ConfigureAwait(false);
                modifiedPaths.Clear();
            }

            try
            {
                var embedded = await _embedder.EmbedChunksAsync(batch, cancellationToken).ConfigureAwait(false);
                progress.AddChunks(batch.Count);
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
            var batch = embedded.Skip(offset).Take(_options.UpsertBatch).ToArray();
            await _vectorStore.UpsertChunksAsync(collection, batch, cancellationToken).ConfigureAwait(false);
        }
    }
}
