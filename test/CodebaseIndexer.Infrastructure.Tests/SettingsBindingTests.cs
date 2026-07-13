using CodebaseIndexer.Infrastructure.Configuration;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace CodebaseIndexer.Infrastructure.Tests;

public sealed class SettingsBindingTests
{
    [Fact]
    public void Connection_strings_override_service_urls()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                [$"{Settings.SectionName}:QdrantUrl"] = "http://localhost:6333",
                [$"{Settings.SectionName}:QdrantTimeoutSeconds"] = "30",
                [$"{Settings.SectionName}:QdrantCollection"] = "codebase",
                [$"{Settings.SectionName}:HybridSearch"] = "true",
                [$"{Settings.SectionName}:DenseEmbedModel"] = "test-model",
                [$"{Settings.SectionName}:SparseEmbedModel"] = "Qdrant/bm25",
                [$"{Settings.SectionName}:DenseEmbedVectorSize"] = "768",
                [$"{Settings.SectionName}:TeiUrl"] = "http://localhost:8080",
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
                ["ConnectionStrings:qdrant"] = "http://qdrant:6333",
                ["ConnectionStrings:tei"] = "http://tei:80",
            })
            .Build();

        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddCodebaseIndexerSettings();

        using var provider = services.BuildServiceProvider(validateScopes: true);
        var settings = provider.GetRequiredService<IOptions<Settings>>().Value;
        Assert.Equal("http://qdrant:6333", settings.QdrantUrl);
        Assert.Equal("http://tei:80", settings.TeiUrl);
    }
}
