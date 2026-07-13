namespace CodebaseIndexer.Infrastructure.Embedding;

public enum EmbedRole
{
    Dense,
    Sparse,
}

public enum TruncationSource
{
    EnvOverride,
    ModelAutoDetect,
    Disabled,
}

public sealed record EmbedTokenLimit(int MaxTokens, TruncationSource Source);

public sealed record TruncatedText(string Text, int TokenCount);
