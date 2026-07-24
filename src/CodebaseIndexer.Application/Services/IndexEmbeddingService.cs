using CodebaseIndexer.Application.Options;
using CodebaseIndexer.Domain.Embedding;
using CodebaseIndexer.Domain.Models;
using CodebaseIndexer.Domain.Ports;
using CodebaseIndexer.Domain.Results;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CodebaseIndexer.Application.Services;

/// <summary>Coordinates dense, sparse, and optional ColBERT embedding with memory-pressure handling.</summary>
public sealed class IndexEmbeddingService : IIndexEmbeddingService
{
    private readonly IDenseEmbedder _dense;
    private readonly ISparseEmbedder _sparse;
    private readonly IColbertEmbedder _colbert;
    private readonly IMemoryPressureGuard _memoryGuard;
    private readonly EmbeddingOptions _embedding;
    private readonly IndexingOptions _indexing;
    private readonly ILogger<IndexEmbeddingService> _logger;

    /// <summary>Creates the embedding service.</summary>
    public IndexEmbeddingService(
        [FromKeyedServices(EmbedderBackendKeys.Dense.Tei)] IDenseEmbedder dense,
        [FromKeyedServices(EmbedderBackendKeys.Sparse.Onnx)] ISparseEmbedder sparse,
        IColbertEmbedder colbert,
        IMemoryPressureGuard memoryGuard,
        IOptions<EmbeddingOptions> embedding,
        IOptions<IndexingOptions> indexing,
        ILogger<IndexEmbeddingService> logger)
    {
        _dense = dense;
        _sparse = sparse;
        _colbert = colbert;
        _memoryGuard = memoryGuard;
        _embedding = embedding.Value;
        _indexing = indexing.Value;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task PreloadAsync(CancellationToken cancellationToken = default)
    {
        var densePreload = await _dense.PreloadAsync(cancellationToken).ConfigureAwait(false);
        if (!densePreload.IsSuccess)
        {
            throw new InvalidOperationException(densePreload.Error.Message);
        }

        if (_embedding.HybridSearch)
        {
            var sparsePreload = await _sparse.PreloadAsync(cancellationToken).ConfigureAwait(false);
            if (!sparsePreload.IsSuccess)
            {
                throw new InvalidOperationException(sparsePreload.Error.Message);
            }
        }

        if (_embedding.RerankEnabled)
        {
            var colbertPreload = await _colbert.PreloadAsync(cancellationToken).ConfigureAwait(false);
            if (!colbertPreload.IsSuccess)
            {
                throw new InvalidOperationException(colbertPreload.Error.Message);
            }
        }
    }

    /// <inheritdoc />
    public void ReleaseModels()
    {
        _dense.Release();
        _sparse.Release();
        if (_embedding.RerankEnabled)
        {
            _colbert.Release();
        }
    }

    /// <inheritdoc />
    public async Task<Result<IReadOnlyList<EmbeddedChunk>>> EmbedChunksAsync(
        IReadOnlyList<Chunk> chunks,
        CancellationToken cancellationToken = default)
    {
        if (chunks.Count == 0)
        {
            return Result<IReadOnlyList<EmbeddedChunk>>.Success(Array.Empty<EmbeddedChunk>());
        }

        var texts = new string[chunks.Count];
        for (var i = 0; i < chunks.Count; i++)
        {
            texts[i] = chunks[i].Content;
        }

        var pressure = _memoryGuard.Check(
            _indexing.MemoryPressureWarnPct,
            _indexing.MemoryPressureHaltPct);

        if (pressure.Severity == MemoryPressureSeverity.Halt)
        {
            return Result<IReadOnlyList<EmbeddedChunk>>.Failure(new Error(
                ErrorKind.Dependency,
                IndexErrorCodes.EmbedMemoryPressure,
                $"Memory pressure {pressure.Percent:F0}% exceeds halt threshold ({_indexing.MemoryPressureHaltPct}%)."));
        }

        var forceSequential = _indexing.SequentialEmbed || pressure.Severity == MemoryPressureSeverity.Warn;
        Result<IReadOnlyList<IReadOnlyList<float>>> denseResult;
        IReadOnlyList<SparseVector>? sparseVectors = null;

        if (_embedding.HybridSearch && !forceSequential)
        {
            var denseTask = _dense.EmbedBatchAsync(texts, cancellationToken);
            var sparseTask = _sparse.EmbedBatchAsync(texts, cancellationToken);
            await Task.WhenAll(denseTask, sparseTask).ConfigureAwait(false);
            denseResult = await denseTask.ConfigureAwait(false);
            var sparseResult = await sparseTask.ConfigureAwait(false);
            if (!sparseResult.IsSuccess)
            {
                return Result<IReadOnlyList<EmbeddedChunk>>.Failure(sparseResult.Error);
            }

            sparseVectors = sparseResult.Value;
        }
        else
        {
            if (forceSequential)
            {
                _logger.LogInformation(
                    "embed_sequential_mode reason={Reason} pressure_pct={Pressure}",
                    _indexing.SequentialEmbed ? "sequential_embed" : "memory_pressure",
                    pressure.Percent);
            }

            denseResult = await _dense.EmbedBatchAsync(texts, cancellationToken).ConfigureAwait(false);
            if (_embedding.HybridSearch)
            {
                var sparseResult = await _sparse.EmbedBatchAsync(texts, cancellationToken).ConfigureAwait(false);
                if (!sparseResult.IsSuccess)
                {
                    return Result<IReadOnlyList<EmbeddedChunk>>.Failure(sparseResult.Error);
                }

                sparseVectors = sparseResult.Value;
            }
        }

        if (!denseResult.IsSuccess)
        {
            return Result<IReadOnlyList<EmbeddedChunk>>.Failure(denseResult.Error);
        }

        IReadOnlyList<IReadOnlyList<IReadOnlyList<float>>>? colbertVectors = null;
        if (_embedding.RerankEnabled)
        {
            var colbertResult = await _colbert.EmbedBatchAsync(texts, cancellationToken).ConfigureAwait(false);
            if (!colbertResult.IsSuccess)
            {
                return Result<IReadOnlyList<EmbeddedChunk>>.Failure(colbertResult.Error);
            }

            colbertVectors = colbertResult.Value;
        }

        var denseVectors = denseResult.Value;
        var embedded = new List<EmbeddedChunk>(chunks.Count);
        for (var i = 0; i < chunks.Count; i++)
        {
            embedded.Add(new EmbeddedChunk(
                chunks[i],
                denseVectors[i],
                _embedding.HybridSearch ? sparseVectors![i] : null)
            {
                ColbertVector = colbertVectors?[i],
            });
        }

        return Result<IReadOnlyList<EmbeddedChunk>>.Success(embedded);
    }
}
