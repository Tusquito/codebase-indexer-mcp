using CodebaseIndexer.Domain.Models;
using CodebaseIndexer.Domain.Results;

namespace CodebaseIndexer.Domain.Ports;

/// <summary>Port for generating sparse embedding vectors from text.</summary>
public interface ISparseEmbedder
{
    /// <summary>Whether the embedder model is loaded and ready for inference.</summary>
    bool IsLoaded { get; }

    /// <summary>
    /// Loads the embedder model into memory.
    /// Cooperative cancel throws <see cref="OperationCanceledException"/>.
    /// </summary>
    /// <param name="cancellationToken">Token used to cancel the preload operation.</param>
    Task<Result> PreloadAsync(CancellationToken cancellationToken = default);

    /// <summary>Releases loaded model resources.</summary>
    void Release();

    /// <summary>
    /// Generates sparse embeddings for a batch of texts.
    /// Cooperative cancel throws <see cref="OperationCanceledException"/>.
    /// </summary>
    /// <param name="texts">Texts to embed.</param>
    /// <param name="cancellationToken">Token used to cancel the operation.</param>
    Task<Result<IReadOnlyList<SparseVector>>> EmbedBatchAsync(
        IReadOnlyList<string> texts,
        CancellationToken cancellationToken = default);
}
