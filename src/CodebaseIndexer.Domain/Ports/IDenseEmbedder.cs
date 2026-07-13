using CodebaseIndexer.Domain.Models;

namespace CodebaseIndexer.Domain.Ports;

/// <summary>Port for generating dense embedding vectors from text.</summary>
public interface IDenseEmbedder
{
    /// <summary>Dimensionality of vectors produced by this embedder.</summary>
    int VectorSize { get; }

    /// <summary>Whether the embedder model is loaded and ready for inference.</summary>
    bool IsLoaded { get; }

    /// <summary>Loads the embedder model into memory.</summary>
    /// <param name="cancellationToken">Token used to cancel the preload operation.</param>
    /// <returns>A task that completes when the model is loaded.</returns>
    Task PreloadAsync(CancellationToken cancellationToken = default);

    /// <summary>Releases loaded model resources.</summary>
    void Release();

    /// <summary>Generates dense embeddings for document texts.</summary>
    /// <param name="texts">Texts to embed as index documents.</param>
    /// <param name="cancellationToken">Token used to cancel the operation.</param>
    /// <returns>Dense embedding vectors in the same order as <paramref name="texts"/>.</returns>
    Task<IReadOnlyList<IReadOnlyList<float>>> EmbedBatchAsync(
        IReadOnlyList<string> texts,
        CancellationToken cancellationToken = default);

    /// <summary>Generates dense embeddings optimized for search queries.</summary>
    /// <param name="texts">Query texts to embed.</param>
    /// <param name="cancellationToken">Token used to cancel the operation.</param>
    /// <returns>Dense query embedding vectors in the same order as <paramref name="texts"/>.</returns>
    Task<IReadOnlyList<IReadOnlyList<float>>> EmbedQueryAsync(
        IReadOnlyList<string> texts,
        CancellationToken cancellationToken = default);
}
