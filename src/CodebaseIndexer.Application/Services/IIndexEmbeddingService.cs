using CodebaseIndexer.Domain.Models;
using CodebaseIndexer.Domain.Results;

namespace CodebaseIndexer.Application.Services;

/// <summary>Embeds code chunks using configured dense and sparse models.</summary>
public interface IIndexEmbeddingService
{
    /// <summary>Preloads embedding models into memory.</summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task PreloadAsync(CancellationToken cancellationToken = default);

    /// <summary>Releases loaded embedding models.</summary>
    void ReleaseModels();

    /// <summary>Embeds a batch of code chunks.</summary>
    /// <param name="chunks">Chunks to embed.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>
    /// Success with embedded chunks, or Failure for expected embedding failures
    /// (memory pressure, dependency/SDK errors). Cooperative cancel throws
    /// <see cref="OperationCanceledException"/>.
    /// </returns>
    Task<Result<IReadOnlyList<EmbeddedChunk>>> EmbedChunksAsync(
        IReadOnlyList<Chunk> chunks,
        CancellationToken cancellationToken = default);
}
