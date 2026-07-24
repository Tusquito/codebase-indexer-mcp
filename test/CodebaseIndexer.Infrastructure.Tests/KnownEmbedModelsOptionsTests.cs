using CodebaseIndexer.Infrastructure.Configuration;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using System.Threading.Tasks;

namespace CodebaseIndexer.Infrastructure.Tests;

/// <summary>Tests for known embedding model defaults and overrides.</summary>
public sealed class KnownEmbedModelsOptionsTests
{
    /// <summary>Finalize applies built-in defaults when configuration is absent.</summary>
    [Test]
    public async Task Finalize_applies_built_in_defaults_when_config_absent()
    {
        var options = ResolveKnownEmbedModels(new ConfigurationBuilder().Build());

        await Assert.That(options.FrozenMaxTokens["jinaai/jina-embeddings-v2-base-code"]).IsEqualTo(8192);
        await Assert.That(options.FrozenMaxTokens["Qwen/Qwen3-Embedding-4B"]).IsEqualTo(32768);
        await Assert.That(options.FrozenMaxTokens["BAAI/bge-base-en-v1.5"]).IsEqualTo(512);
    }

    /// <summary>Configuration overrides default max token values.</summary>
    [Test]
    public async Task Configuration_overrides_default_max_tokens()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                [$"{KnownEmbedModelsOptions.SectionName}:MaxTokens:custom/model"] = "1024",
                [$"{KnownEmbedModelsOptions.SectionName}:MaxTokens:jinaai/jina-embeddings-v2-base-code"] = "4096",
            })
            .Build();

        var options = ResolveKnownEmbedModels(configuration);

        await Assert.That(options.FrozenMaxTokens["jinaai/jina-embeddings-v2-base-code"]).IsEqualTo(4096);
        await Assert.That(options.FrozenMaxTokens["custom/model"]).IsEqualTo(1024);
        await Assert.That(options.FrozenMaxTokens["nomic-ai/nomic-embed-text-v1.5"]).IsEqualTo(8192);
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