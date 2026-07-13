namespace CodebaseIndexer.Infrastructure.Embedding;

/// <summary>Resolved maximum token count and how it was determined.</summary>
/// <param name="MaxTokens">Maximum tokens allowed; 0 means no truncation.</param>
/// <param name="Source">How the limit was resolved.</param>
public sealed record EmbedTokenLimit(int MaxTokens, TruncationSource Source);
