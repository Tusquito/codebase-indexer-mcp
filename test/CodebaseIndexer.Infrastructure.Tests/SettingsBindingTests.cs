using CodebaseIndexer.Infrastructure.Configuration;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace CodebaseIndexer.Infrastructure.Tests;

public sealed class SettingsBindingTests
{
    [Fact]
    public void BindConfiguration_uses_section_urls_when_connection_strings_absent()
    {
        var configuration = CreateConfiguration(
            sectionQdrantUrl: "http://localhost:6333",
            sectionTeiUrl: "http://localhost:8080");

        var settings = ResolveSettings(configuration);

        Assert.Equal("http://localhost:6333", settings.QdrantUrl);
        Assert.Equal("http://localhost:8080", settings.TeiUrl);
        Assert.Equal("codebase", settings.QdrantCollection);
        Assert.Equal(1, settings.HashWorkerDop);
    }

    [Fact]
    public void Connection_strings_override_section_service_urls()
    {
        var configuration = CreateConfiguration(
            sectionQdrantUrl: "http://localhost:6333",
            sectionTeiUrl: "http://localhost:8080",
            connectionQdrantUrl: "http://qdrant:6333",
            connectionTeiUrl: "http://tei:80");

        var settings = ResolveSettings(configuration);

        Assert.Equal("http://qdrant:6333", settings.QdrantUrl);
        Assert.Equal("http://tei:80", settings.TeiUrl);
    }

    [Fact]
    public void ValidateOnStart_fails_when_required_section_fields_missing()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                [$"{Settings.SectionName}:QdrantUrl"] = "http://localhost:6333",
            })
            .Build();

        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddCodebaseIndexerSettings();

        using var provider = services.BuildServiceProvider(validateScopes: true);

        var exception = Assert.Throws<OptionsValidationException>(() =>
            _ = provider.GetRequiredService<IOptions<Settings>>().Value);

        Assert.Contains(nameof(Settings.TeiUrl), exception.Message, StringComparison.Ordinal);
    }

    private static Settings ResolveSettings(IConfiguration configuration)
    {
        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddCodebaseIndexerSettings();

        using var provider = services.BuildServiceProvider(validateScopes: true);
        return provider.GetRequiredService<IOptions<Settings>>().Value;
    }

    private static IConfiguration CreateConfiguration(
        string sectionQdrantUrl,
        string sectionTeiUrl,
        string? connectionQdrantUrl = null,
        string? connectionTeiUrl = null)
    {
        var values = new Dictionary<string, string?>
        {
            [$"{Settings.SectionName}:QdrantUrl"] = sectionQdrantUrl,
            [$"{Settings.SectionName}:QdrantTimeoutSeconds"] = "30",
            [$"{Settings.SectionName}:QdrantCollection"] = "codebase",
            [$"{Settings.SectionName}:HybridSearch"] = "true",
            [$"{Settings.SectionName}:DenseEmbedModel"] = "test-model",
            [$"{Settings.SectionName}:SparseEmbedModel"] = "Qdrant/bm25",
            [$"{Settings.SectionName}:DenseEmbedVectorSize"] = "768",
            [$"{Settings.SectionName}:TeiUrl"] = sectionTeiUrl,
            [$"{Settings.SectionName}:TeiEmbedBatchSize"] = "32",
            [$"{Settings.SectionName}:TeiTimeoutSeconds"] = "120",
            [$"{Settings.SectionName}:QueryInstruction"] = string.Empty,
            [$"{Settings.SectionName}:NormalizeOutput"] = "false",
            [$"{Settings.SectionName}:RerankEnabled"] = "false",
            [$"{Settings.SectionName}:PayloadIndexes"] = "true",
            [$"{Settings.SectionName}:VectorsOnDisk"] = "false",
            [$"{Settings.SectionName}:SparseOnDisk"] = "false",
            [$"{Settings.SectionName}:WorkspacePath"] = "/workspace",
            [$"{Settings.SectionName}:MaxChunkLines"] = "150",
            [$"{Settings.SectionName}:ChunkOverlapLines"] = "20",
            [$"{Settings.SectionName}:BatchSize"] = "32",
            [$"{Settings.SectionName}:FlushEvery"] = "1500",
            [$"{Settings.SectionName}:UpsertBatch"] = "500",
            [$"{Settings.SectionName}:ReadaheadBuffer"] = "100",
            [$"{Settings.SectionName}:HashWorkerDop"] = "1",
            [$"{Settings.SectionName}:MaxDenseEmbedTokens"] = "0",
            [$"{Settings.SectionName}:MaxSparseEmbedTokens"] = "0",
            [$"{Settings.SectionName}:SparseThreads"] = "2",
            [$"{Settings.SectionName}:SequentialEmbed"] = "false",
            [$"{Settings.SectionName}:MemoryPressureWarnPct"] = "70",
            [$"{Settings.SectionName}:MemoryPressureHaltPct"] = "85",
            [$"{Settings.SectionName}:ReleaseModelsAfterIndex"] = "true",
            [$"{Settings.SectionName}:ModelIdleTimeoutSeconds"] = "300",
            [$"{Settings.SectionName}:PreloadModels"] = "true",
            [$"{Settings.SectionName}:FastembedCachePath"] = "/root/.cache/fastembed",
            [$"{Settings.SectionName}:ExcludedDirs"] = "node_modules,.git",
        };

        if (connectionQdrantUrl is not null)
        {
            values["ConnectionStrings:qdrant"] = connectionQdrantUrl;
        }

        if (connectionTeiUrl is not null)
        {
            values["ConnectionStrings:tei"] = connectionTeiUrl;
        }

        return new ConfigurationBuilder()
            .AddInMemoryCollection(values)
            .Build();
    }
}
