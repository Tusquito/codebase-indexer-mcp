using CodebaseIndexer.Domain.Exceptions;
using CodebaseIndexer.Domain.Ports;

namespace CodebaseIndexer.Infrastructure.Colbert;

/// <summary>No-op ColBERT embedder used when rerank is disabled.</summary>
public sealed class NullColbertEmbedder : IColbertEmbedder
{
    /// <inheritdoc />
    public int TokenDimension => 0;

    /// <inheritdoc />
    public bool IsLoaded => false;

    /// <inheritdoc />
    public Task PreloadAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

    /// <inheritdoc />
    public void Release()
    {
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<IReadOnlyList<IReadOnlyList<float>>>> EmbedBatchAsync(
        IReadOnlyList<string> texts,
        CancellationToken cancellationToken = default) =>
        throw new EmbeddingException("ColBERT embedder is not configured (Embedding:RerankEnabled=false).");
}
