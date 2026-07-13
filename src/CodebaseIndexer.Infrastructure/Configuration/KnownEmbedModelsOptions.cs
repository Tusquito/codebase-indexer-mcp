using System.Collections.Frozen;

namespace CodebaseIndexer.Infrastructure.Configuration;

/// <summary>Registry of known embedding models and their token limits.</summary>
public sealed class KnownEmbedModelsOptions
{
    /// <summary>Configuration section name.</summary>
    public const string SectionName = "KnownEmbedModels";

    /// <summary>Model name to maximum token count mapping.</summary>
    public FrozenDictionary<string, int> FrozenMaxTokens { get; init; } =
        FrozenDictionary<string, int>.Empty;
}
