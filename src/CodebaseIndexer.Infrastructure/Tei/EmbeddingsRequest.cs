namespace CodebaseIndexer.Infrastructure.Tei;

/// <summary>OpenAI-compatible embeddings API request body.</summary>
/// <param name="Model">Model identifier to use for embedding.</param>
/// <param name="Input">Texts to embed.</param>
/// <param name="Dimensions">Optional output dimensionality for MRL models.</param>
public sealed record EmbeddingsRequest(string Model, IReadOnlyList<string> Input, int? Dimensions = null);
