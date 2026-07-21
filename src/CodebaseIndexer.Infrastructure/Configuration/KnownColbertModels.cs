namespace CodebaseIndexer.Infrastructure.Configuration;

/// <summary>Known ColBERT token dimensions and max tokens (parity with Python KNOWN_COLBERT_*).</summary>
public static class KnownColbertModels
{
    /// <summary>Default token dimension when model is unknown.</summary>
    public const int DefaultTokenDimension = 128;

    /// <summary>Token dimensions by model id.</summary>
    public static IReadOnlyDictionary<string, int> TokenDimensions { get; } =
        new Dictionary<string, int>(StringComparer.Ordinal)
        {
            ["colbert-ir/colbertv2.0"] = 128,
        };

    /// <summary>Max sequence tokens by model id.</summary>
    public static IReadOnlyDictionary<string, int> MaxTokens { get; } =
        new Dictionary<string, int>(StringComparer.Ordinal)
        {
            ["colbert-ir/colbertv2.0"] = 512,
        };

    /// <summary>Resolves token dimension for a ColBERT model id.</summary>
    public static int ResolveTokenDimension(string modelName) =>
        TokenDimensions.TryGetValue(modelName, out var dim) ? dim : DefaultTokenDimension;
}
