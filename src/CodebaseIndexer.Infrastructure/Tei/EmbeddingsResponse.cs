namespace CodebaseIndexer.Infrastructure.Tei;

/// <summary>OpenAI-compatible embeddings API response body.</summary>
/// <param name="Data">Embedding results, one per input text.</param>
public sealed record EmbeddingsResponse(IReadOnlyList<EmbeddingData> Data);
