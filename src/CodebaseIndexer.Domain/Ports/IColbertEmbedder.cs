using CodebaseIndexer.Domain.Results;

namespace CodebaseIndexer.Domain.Ports;

/// <summary>Port for ColBERT late-interaction multivector embeddings.</summary>
public interface IColbertEmbedder
{
    /// <summary>Token embedding dimensionality (e.g. 128 for colbertv2.0).</summary>
    int TokenDimension { get; }

    /// <summary>Whether the embedder model or remote sidecar is ready.</summary>
    bool IsLoaded { get; }

    /// <summary>
    /// Loads the model or probes the remote sidecar.
    /// Cooperative cancel throws <see cref="OperationCanceledException"/>.
    /// </summary>
    Task<Result> PreloadAsync(CancellationToken cancellationToken = default);

    /// <summary>Releases loaded model resources (no-op for remote).</summary>
    void Release();

    /// <summary>
    /// Embeds texts into per-token multivectors.
    /// Cooperative cancel throws <see cref="OperationCanceledException"/>.
    /// </summary>
    /// <returns>One multivector (list of token vectors) per input text on success.</returns>
    Task<Result<IReadOnlyList<IReadOnlyList<IReadOnlyList<float>>>>> EmbedBatchAsync(
        IReadOnlyList<string> texts,
        CancellationToken cancellationToken = default);
}
