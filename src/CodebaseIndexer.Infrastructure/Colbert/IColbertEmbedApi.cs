using Refit;

namespace CodebaseIndexer.Infrastructure.Colbert;

/// <summary>Typed Refit client for the ColBERT HTTP sidecar (ADR 0015).</summary>
public interface IColbertEmbedApi
{
    /// <summary>Checks ColBERT worker health.</summary>
    [Get("/health")]
    Task<ColbertHealthResponse> GetHealthAsync(CancellationToken cancellationToken = default);

    /// <summary>Embeds texts into ColBERT multivectors.</summary>
    [Post("/v1/embed/colbert")]
    Task<ColbertEmbedResponse> EmbedColbertAsync(
        [Body] ColbertEmbedRequest request,
        CancellationToken cancellationToken = default);
}
