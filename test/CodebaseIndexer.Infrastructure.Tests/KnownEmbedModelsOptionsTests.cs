using CodebaseIndexer.Infrastructure.Configuration;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace CodebaseIndexer.Infrastructure.Tests;

/// <summary>Tests for known embedding model defaults and overrides.</summary>
public sealed class KnownEmbedModelsOptionsTests
{
    /// <summary>Finalize applies built-in defaults when configuration is absent.</summary>
    [Fact]
    public void Finalize_applies_built_in_defaults_when_config_absent()
    {
        var options = ResolveKnownEmbedModels(new ConfigurationBuilder().Build());

        Assert.Equal(8192, options.FrozenMaxTokens["jinaai/jina-embeddings-v2-base-code"]);
        Assert.Equal(32768, options.FrozenMaxTokens["Qwen/Qwen3-Embedding-4B"]);
        Assert.Equal(512, options.FrozenMaxTokens["BAAI/bge-base-en-v1.5"]);
    }

    /// <summary>Configuration overrides default max token values.</summary>
    [Fact]
    public void Configuration_overrides_default_max_tokens()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                [$"{KnownEmbedModelsOptions.SectionName}:MaxTokens:custom/model"] = "1024",
                [$"{KnownEmbedModelsOptions.SectionName}:MaxTokens:jinaai/jina-embeddings-v2-base-code"] = "4096",
            })
            .Build();

        var options = ResolveKnownEmbedModels(configuration);

        Assert.Equal(4096, options.FrozenMaxTokens["jinaai/jina-embeddings-v2-base-code"]);
        Assert.Equal(1024, options.FrozenMaxTokens["custom/model"]);
        Assert.Equal(8192, options.FrozenMaxTokens["nomic-ai/nomic-embed-text-v1.5"]);
    }

    private static KnownEmbedModelsOptions ResolveKnownEmbedModels(IConfiguration configuration)
    {
        var services = new ServiceCollection();
        services.AddSingleton(configuration);
        services.AddCodebaseIndexerSettings();

        using var provider = services.BuildServiceProvider();
        return provider.GetRequiredService<IOptions<KnownEmbedModelsOptions>>().Value;
    }
}
