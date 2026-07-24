using CodebaseIndexer.Domain.Ports;
using CodebaseIndexer.Domain.Results;

namespace CodebaseIndexer.Infrastructure.Colbert;

/// <summary>No-op ColBERT embedder used when rerank is disabled.</summary>
public sealed class NullColbertEmbedder : IColbertEmbedder
{
    /// <inheritdoc />
    public int TokenDimension => 0;

    /// <inheritdoc />
    public bool IsLoaded => false;

    /// <inheritdoc />
    public Task<Result> PreloadAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(Result.Success());

    /// <inheritdoc />
    public void Release()
    {
    }

    /// <inheritdoc />
    public Task<Result<IReadOnlyList<IReadOnlyList<IReadOnlyList<float>>>>> EmbedBatchAsync(
        IReadOnlyList<string> texts,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(Result<IReadOnlyList<IReadOnlyList<IReadOnlyList<float>>>>.Failure(new Error(
            ErrorKind.Dependency,
            EmbedErrorCodes.NotConfigured,
            "ColBERT embedder is not configured (Embedding:RerankEnabled=false).")));
}
