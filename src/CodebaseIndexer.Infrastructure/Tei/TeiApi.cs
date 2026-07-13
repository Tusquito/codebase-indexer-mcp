using Refit;

namespace CodebaseIndexer.Infrastructure.Tei;

public sealed record EmbeddingsRequest(string Model, IReadOnlyList<string> Input, int? Dimensions = null);

public sealed record EmbeddingData(IReadOnlyList<float> Embedding, int Index);

public sealed record EmbeddingsResponse(IReadOnlyList<EmbeddingData> Data);

/// <summary>Typed Refit client for the TEI HTTP API (health + OpenAI-compatible embeddings).</summary>
public interface ITeiEmbeddingsApi
{
    [Get("/health")]
    Task<HttpResponseMessage> GetHealthAsync(CancellationToken cancellationToken = default);

    [Post("/v1/embeddings")]
    Task<EmbeddingsResponse> CreateEmbeddingsAsync(
        [Body] EmbeddingsRequest request,
        CancellationToken cancellationToken = default);
}
