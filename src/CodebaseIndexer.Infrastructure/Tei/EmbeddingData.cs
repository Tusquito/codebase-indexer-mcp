namespace CodebaseIndexer.Infrastructure.Tei;

/// <summary>Single embedding vector from a TEI response.</summary>
/// <param name="Embedding">Dense float vector values.</param>
/// <param name="Index">Zero-based index matching the request input order.</param>
public sealed record EmbeddingData(IReadOnlyList<float> Embedding, int Index);
