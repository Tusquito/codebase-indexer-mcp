using CodebaseIndexer.Application.Options;
using CodebaseIndexer.Domain.Exceptions;
using CodebaseIndexer.Domain.Models;
using CodebaseIndexer.Domain.Ports;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CodebaseIndexer.Application.Services;

public interface IIndexEmbeddingService
{
    Task PreloadAsync(CancellationToken cancellationToken = default);
    void ReleaseModels();
    Task<IReadOnlyList<EmbeddedChunk>> EmbedChunksAsync(
        IReadOnlyList<Chunk> chunks,
        CancellationToken cancellationToken = default);
}

public sealed class IndexEmbeddingService : IIndexEmbeddingService
{
    private readonly IDenseEmbedder _dense;
    private readonly ISparseEmbedder _sparse;
    private readonly IMemoryPressureGuard _memoryGuard;
    private readonly IndexingOptions _options;
    private readonly ILogger<IndexEmbeddingService> _logger;

    public IndexEmbeddingService(
        IDenseEmbedder dense,
        ISparseEmbedder sparse,
        IMemoryPressureGuard memoryGuard,
        IOptions<IndexingOptions> options,
        ILogger<IndexEmbeddingService> logger)
    {
        _dense = dense;
        _sparse = sparse;
        _memoryGuard = memoryGuard;
        _options = options.Value;
        _logger = logger;
    }

    public async Task PreloadAsync(CancellationToken cancellationToken = default)
    {
        await _dense.PreloadAsync(cancellationToken).ConfigureAwait(false);
        if (_options.HybridSearch)
        {
            await _sparse.PreloadAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    public void ReleaseModels()
    {
        _dense.Release();
        _sparse.Release();
    }

    public async Task<IReadOnlyList<EmbeddedChunk>> EmbedChunksAsync(
        IReadOnlyList<Chunk> chunks,
        CancellationToken cancellationToken = default)
    {
        if (chunks.Count == 0)
        {
            return Array.Empty<EmbeddedChunk>();
        }

        var texts = chunks.Select(c => c.Content).ToArray();
        var (severity, pct) = _memoryGuard.Check(
            _options.MemoryPressureWarnPct,
            _options.MemoryPressureHaltPct);

        if (severity == MemoryPressureSeverity.Halt)
        {
            throw new EmbeddingException(
                $"Memory pressure {pct:F0}% exceeds halt threshold ({_options.MemoryPressureHaltPct}%).");
        }

        var forceSequential = _options.SequentialEmbed || severity == MemoryPressureSeverity.Warn;
        IReadOnlyList<IReadOnlyList<float>> denseVectors;
        IReadOnlyList<SparseVector>? sparseVectors = null;

        if (_options.HybridSearch && !forceSequential)
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
                    _options.SequentialEmbed ? "sequential_embed" : "memory_pressure",
                    pct);
            }

            denseVectors = await _dense.EmbedBatchAsync(texts, cancellationToken).ConfigureAwait(false);
            if (_options.HybridSearch)
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
                _options.HybridSearch ? sparseVectors![i] : null));
        }

        return embedded;
    }
}
