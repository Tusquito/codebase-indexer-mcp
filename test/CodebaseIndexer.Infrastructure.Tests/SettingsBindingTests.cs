using CodebaseIndexer.Application;
using CodebaseIndexer.Application.Options;
using CodebaseIndexer.Infrastructure.Configuration;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace CodebaseIndexer.Infrastructure.Tests;

/// <summary>Tests for configuration binding and validate-on-start.</summary>
public sealed class SettingsBindingTests
{
    /// <summary>Configuration binds split option sections correctly.</summary>
    [Fact]
    public void BindConfiguration_binds_split_sections()
    {
        var configuration = TestSettingsFactory.CreateConfiguration();

        var qdrant = ResolveOptions<QdrantOptions>(configuration);
        var tei = ResolveOptions<TeiOptions>(configuration);

        Assert.Equal("http://localhost:6334", qdrant.Url);
        Assert.Equal("http://localhost:8080", tei.Url);
        Assert.Equal("codebase", qdrant.Collection);
    }

    /// <summary>Later configuration sources override section URLs.</summary>
    [Fact]
    public void Later_configuration_overrides_section_urls()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(TestSettingsFactory.CreateConfigurationValues())
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                [$"{QdrantOptions.SectionName}:Url"] = "http://qdrant:6334",
                [$"{TeiOptions.SectionName}:Url"] = "http://tei:80",
            })
            .Build();

        var qdrant = ResolveOptions<QdrantOptions>(configuration);
        var tei = ResolveOptions<TeiOptions>(configuration);

        Assert.Equal("http://qdrant:6334", qdrant.Url);
        Assert.Equal("http://tei:80", tei.Url);
    }

    /// <summary>Validate-on-start fails when required TEI URL is missing.</summary>
    [Fact]
    public void ValidateOnStart_fails_when_required_tei_url_missing()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                [$"{QdrantOptions.SectionName}:Url"] = "http://localhost:6334",
                [$"{QdrantOptions.SectionName}:TimeoutSeconds"] = "30",
                [$"{QdrantOptions.SectionName}:Collection"] = "codebase",
                [$"{EmbeddingOptions.SectionName}:DenseModel"] = "test-model",
                [$"{EmbeddingOptions.SectionName}:SparseModel"] = "Qdrant/bm25",
                [$"{EmbeddingOptions.SectionName}:DenseVectorSize"] = "768",
                [$"{EmbeddingOptions.SectionName}:CachePath"] = "/cache",
            })
            .Build();

        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddCodebaseIndexerSettings();
        services.AddCodebaseIndexerApplication();

        using var provider = services.BuildServiceProvider(validateScopes: true);

        var exception = Assert.Throws<OptionsValidationException>(() =>
            _ = provider.GetRequiredService<IOptions<TeiOptions>>().Value);

        Assert.Contains(nameof(TeiOptions.Url), exception.Message, StringComparison.Ordinal);
    }

    private static TOptions ResolveOptions<TOptions>(IConfiguration configuration)
        where TOptions : class
    {
        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddCodebaseIndexerSettings();
        services.AddCodebaseIndexerApplication();

        using var provider = services.BuildServiceProvider(validateScopes: true);
        return provider.GetRequiredService<IOptions<TOptions>>().Value;
    }
}
