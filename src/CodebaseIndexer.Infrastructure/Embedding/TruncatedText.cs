namespace CodebaseIndexer.Infrastructure.Embedding;

/// <summary>Text truncated to a token limit with the resulting token count.</summary>
/// <param name="Text">The possibly truncated text.</param>
/// <param name="TokenCount">Token count after truncation, or -1 when not measured.</param>
public sealed record TruncatedText(string Text, int TokenCount);
