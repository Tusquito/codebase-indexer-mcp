using CodebaseIndexer.Domain.Models;

namespace CodebaseIndexer.Domain.Ports;

/// <summary>Port for generating sparse embedding vectors from text.</summary>
public interface ISparseEmbedder
{
    /// <summary>Whether the embedder model is loaded and ready for inference.</summary>
    bool IsLoaded { get; }

    /// <summary>Loads the embedder model into memory.</summary>
    /// <param name="cancellationToken">Token used to cancel the preload operation.</param>
    /// <returns>A task that completes when the model is loaded.</returns>
    Task PreloadAsync(CancellationToken cancellationToken = default);

    /// <summary>Releases loaded model resources.</summary>
    void Release();

    /// <summary>Generates sparse embeddings for a batch of texts.</summary>
    /// <param name="texts">Texts to embed.</param>
    /// <param name="cancellationToken">Token used to cancel the operation.</param>
    /// <returns>Sparse embedding vectors in the same order as <paramref name="texts"/>.</returns>
    Task<IReadOnlyList<SparseVector>> EmbedBatchAsync(
        IReadOnlyList<string> texts,
        CancellationToken cancellationToken = default);
}
