using System.Text.Json.Serialization;

namespace CodebaseIndexer.Infrastructure.Colbert;

/// <summary>POST /v1/embed/colbert response body.</summary>
public sealed record ColbertEmbedResponse(
    [property: JsonPropertyName("embeddings")] IReadOnlyList<IReadOnlyList<IReadOnlyList<float>>> Embeddings,
    [property: JsonPropertyName("token_dimension")] int TokenDimension);
