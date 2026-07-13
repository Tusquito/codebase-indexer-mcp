using CodebaseIndexer.Application.Options;
using CodebaseIndexer.Domain.Embedding;
using CodebaseIndexer.Domain.Exceptions;
using CodebaseIndexer.Domain.Models;
using CodebaseIndexer.Domain.Ports;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CodebaseIndexer.Application.Services;

/// <summary>Coordinates dense and sparse embedding with memory-pressure handling.</summary>
public sealed class IndexEmbeddingService : IIndexEmbeddingService
{
    private readonly IDenseEmbedder _dense;
    private readonly ISparseEmbedder _sparse;
    private readonly IMemoryPressureGuard _memoryGuard;
    private readonly EmbeddingOptions _embedding;
    private readonly IndexingOptions _indexing;
    private readonly ILogger<IndexEmbeddingService> _logger;

    /// <summary>Creates the embedding service.</summary>
    /// <param name="dense">Dense embedder backend.</param>
    /// <param name="sparse">Sparse embedder backend.</param>
    /// <param name="memoryGuard">Memory pressure guard.</param>
    /// <param name="embedding">Embedding model options.</param>
    /// <param name="indexing">Indexing pipeline options.</param>
    /// <param name="logger">Logger instance.</param>
    public IndexEmbeddingService(
        [FromKeyedServices(EmbedderBackendKeys.Dense.Tei)] IDenseEmbedder dense,
        [FromKeyedServices(EmbedderBackendKeys.Sparse.Onnx)] ISparseEmbedder sparse,
        IMemoryPressureGuard memoryGuard,
        IOptions<EmbeddingOptions> embedding,
        IOptions<IndexingOptions> indexing,
        ILogger<IndexEmbeddingService> logger)
    {
        _dense = dense;
        _sparse = sparse;
        _memoryGuard = memoryGuard;
        _embedding = embedding.Value;
        _indexing = indexing.Value;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task PreloadAsync(CancellationToken cancellationToken = default)
    {
        await _dense.PreloadAsync(cancellationToken).ConfigureAwait(false);
        if (_embedding.HybridSearch)
        {
            await _sparse.PreloadAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    /// <inheritdoc />
    public void ReleaseModels()
    {
        _dense.Release();
        _sparse.Release();
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<EmbeddedChunk>> EmbedChunksAsync(
        IReadOnlyList<Chunk> chunks,
        CancellationToken cancellationToken = default)
    {
        if (chunks.Count == 0)
        {
            return Array.Empty<EmbeddedChunk>();
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
            throw new EmbeddingException(
                $"Memory pressure {pressure.Percent:F0}% exceeds halt threshold ({_indexing.MemoryPressureHaltPct}%).");
        }

        var forceSequential = _indexing.SequentialEmbed || pressure.Severity == MemoryPressureSeverity.Warn;
        IReadOnlyList<IReadOnlyList<float>> denseVectors;
        IReadOnlyList<SparseVector>? sparseVectors = null;

        if (_embedding.HybridSearch && !forceSequential)
        {
            var denseTask = _dense.EmbedBatchAsync(texts, cancellationToken);
            var sparseTask = _sparse.EmbedBatchAsync(texts, cancellationToken);
            await Task.WhenAll(denseTask, sparseTask).ConfigureAwait(false);
            denseVectors = await denseTask.ConfigureAwait(false);
            sparseVectors = await sparseTask.ConfigureAwait(false);
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

            denseVectors = await _dense.EmbedBatchAsync(texts, cancellationToken).ConfigureAwait(false);
            if (_embedding.HybridSearch)
            {
                sparseVectors = await _sparse.EmbedBatchAsync(texts, cancellationToken).ConfigureAwait(false);
            }
        }

        var embedded = new List<EmbeddedChunk>(chunks.Count);
        for (var i = 0; i < chunks.Count; i++)
        {
            embedded.Add(new EmbeddedChunk(
                chunks[i],
                denseVectors[i],
                _embedding.HybridSearch ? sparseVectors![i] : null));
        }

        return embedded;
    }
}
