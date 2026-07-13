using System.Collections.Frozen;
using Microsoft.Extensions.Configuration;

namespace CodebaseIndexer.Infrastructure.Configuration;

internal static class KnownEmbedModelsFactory
{
    public static KnownEmbedModelsOptions Create(IConfiguration configuration)
    {
        var fromConfig = new Dictionary<string, int>(StringComparer.Ordinal);
        configuration.GetSection($"{KnownEmbedModelsOptions.SectionName}:MaxTokens").Bind(fromConfig);

        var merged = new Dictionary<string, int>(KnownEmbedModelsDefaults.MaxTokens, StringComparer.Ordinal);
        foreach (var (model, maxTokens) in fromConfig)
        {
            merged[model] = maxTokens;
        }

        return new KnownEmbedModelsOptions
        {
            FrozenMaxTokens = merged.ToFrozenDictionary(StringComparer.Ordinal),
        };
    }
}
