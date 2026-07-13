using Refit;

namespace CodebaseIndexer.Infrastructure.Tei;

/// <summary>Typed Refit client for the TEI HTTP API (health + OpenAI-compatible embeddings).</summary>
public interface ITeiEmbeddingsApi
{
    /// <summary>Checks TEI service health.</summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>HTTP response from the health endpoint.</returns>
    [Get("/health")]
    Task<HttpResponseMessage> GetHealthAsync(CancellationToken cancellationToken = default);

    /// <summary>Creates embeddings for a batch of input texts.</summary>
    /// <param name="request">Embedding request payload.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Embedding vectors ordered by input index.</returns>
    [Post("/v1/embeddings")]
    Task<EmbeddingsResponse> CreateEmbeddingsAsync(
        [Body] EmbeddingsRequest request,
        CancellationToken cancellationToken = default);
}
