using Refit;

namespace CodebaseIndexer.Infrastructure.Tei;

public sealed record EmbeddingsRequest(string Model, IReadOnlyList<string> Input, int? Dimensions = null);

public sealed record EmbeddingData(IReadOnlyList<float> Embedding, int Index);

public sealed record EmbeddingsResponse(IReadOnlyList<EmbeddingData> Data);

public interface ITeiEmbeddingsApi
{
    [Post("/v1/embeddings")]
    Task<EmbeddingsResponse> CreateEmbeddingsAsync(
        [Body] EmbeddingsRequest request,
        CancellationToken cancellationToken = default);
}
