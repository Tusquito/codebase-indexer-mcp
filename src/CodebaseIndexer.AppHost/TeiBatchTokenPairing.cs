namespace CodebaseIndexer.AppHost;

/// <summary>
/// Resolves paired TEI <c>--max-batch-tokens</c> and MCP client dense-token caps (ADR 0035).
/// </summary>
public static class TeiBatchTokenPairing
{
    /// <summary>Default paired cap when env is missing or blank.</summary>
    public const string DefaultTokens = "1024";

    /// <summary>
    /// Resolves TEI <c>--max-batch-tokens</c> from <c>TEI_MAX_BATCH_TOKENS</c>.
    /// </summary>
    /// <param name="teiMaxBatchTokens">Raw <c>TEI_MAX_BATCH_TOKENS</c> value, or null.</param>
    /// <returns>Trimmed override, or <see cref="DefaultTokens"/> when missing/blank.</returns>
    public static string ResolveTeiMaxBatchTokens(string? teiMaxBatchTokens)
        => string.IsNullOrWhiteSpace(teiMaxBatchTokens) ? DefaultTokens : teiMaxBatchTokens.Trim();

    /// <summary>
    /// Resolves MCP <c>Embedding__MaxDenseTokens</c> from nested then flat env names.
    /// </summary>
    /// <param name="embeddingMaxDenseTokens">Raw <c>Embedding__MaxDenseTokens</c>, or null.</param>
    /// <param name="maxDenseEmbedTokens">Raw <c>MAX_DENSE_EMBED_TOKENS</c>, or null.</param>
    /// <returns>
    /// Nested override, else flat override, else <see cref="DefaultTokens"/> when both missing/blank.
    /// </returns>
    public static string ResolveClientMaxDenseTokens(
        string? embeddingMaxDenseTokens,
        string? maxDenseEmbedTokens)
    {
        if (!string.IsNullOrWhiteSpace(embeddingMaxDenseTokens))
        {
            return embeddingMaxDenseTokens.Trim();
        }

        if (!string.IsNullOrWhiteSpace(maxDenseEmbedTokens))
        {
            return maxDenseEmbedTokens.Trim();
        }

        return DefaultTokens;
    }
}
