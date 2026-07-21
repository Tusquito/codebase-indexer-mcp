using CodebaseIndexer.Application.Options;
using CodebaseIndexer.Infrastructure.Configuration;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

namespace CodebaseIndexer.Host.Tests;

/// <summary>Web application factory with in-memory test configuration.</summary>
public class McpHostWebApplicationFactory : WebApplicationFactory<Program>
{
    private readonly IReadOnlyDictionary<string, string?> _earlySettings;

    /// <summary>Creates a factory with default test configuration (Graph disabled).</summary>
    public McpHostWebApplicationFactory()
        : this(null)
    {
    }

    /// <summary>
    /// Creates a factory with optional early host settings (for subclasses / direct construction).
    /// Early values are applied via <see cref="IHostBuilder.ConfigureHostConfiguration"/> so they are
    /// visible when <c>AddCodebaseIndexerHost</c> gates MCP <c>WithTools</c>.
    /// </summary>
    /// <param name="earlySettings">Settings merged into host + app configuration.</param>
    protected McpHostWebApplicationFactory(IReadOnlyDictionary<string, string?>? earlySettings)
    {
        _earlySettings = earlySettings ?? new Dictionary<string, string?>(StringComparer.Ordinal);
    }

    /// <inheritdoc />
    protected override IHost CreateHost(IHostBuilder builder)
    {
        // Host configuration must include Reindex:Enabled=false before Program runs
        // AddCodebaseIndexerHost (Quartz registers from builder.Configuration at startup).
        builder.ConfigureHostConfiguration(config =>
        {
            var hostDefaults = new Dictionary<string, string?>(StringComparer.Ordinal)
            {
                [$"{ReindexOptions.SectionName}:Enabled"] = "false",
            };
            foreach (var (key, value) in _earlySettings)
            {
                hostDefaults[key] = value;
            }

            config.AddInMemoryCollection(hostDefaults);
        });

        return base.CreateHost(builder);
    }

    /// <inheritdoc />
    protected override void ConfigureWebHost(Microsoft.AspNetCore.Hosting.IWebHostBuilder builder)
    {
        builder.ConfigureAppConfiguration((_, config) =>
        {
            var values = CreateDefaultSettings();
            foreach (var (key, value) in _earlySettings)
            {
                values[key] = value;
            }

            config.AddInMemoryCollection(values);
        });
    }

    private static Dictionary<string, string?> CreateDefaultSettings() =>
        new(StringComparer.Ordinal)
        {
            [$"{QdrantOptions.SectionName}:Url"] = "http://localhost:6334",
            [$"{QdrantOptions.SectionName}:TimeoutSeconds"] = "30",
            [$"{QdrantOptions.SectionName}:Collection"] = "codebase",
            [$"{QdrantOptions.SectionName}:PayloadIndexes"] = "true",
            [$"{QdrantOptions.SectionName}:VectorsOnDisk"] = "false",
            [$"{QdrantOptions.SectionName}:SparseOnDisk"] = "false",
            [$"{QdrantOptions.SectionName}:Quantization"] = "true",
            [$"{QdrantOptions.SectionName}:HnswEf"] = "64",
            [$"{QdrantOptions.SectionName}:HnswM"] = "16",
            [$"{QdrantOptions.SectionName}:HnswEfConstruct"] = "128",
            [$"{QdrantOptions.SectionName}:QuantOversampling"] = "2.0",
            [$"{QdrantOptions.SectionName}:MemmapThresholdKb"] = "20000",
            [$"{EmbeddingOptions.SectionName}:HybridSearch"] = "true",
            [$"{EmbeddingOptions.SectionName}:DenseModel"] = "test-model",
            [$"{EmbeddingOptions.SectionName}:SparseModel"] = "Qdrant/bm25",
            [$"{EmbeddingOptions.SectionName}:DenseVectorSize"] = "768",
            [$"{EmbeddingOptions.SectionName}:RerankEnabled"] = "false",
            [$"{EmbeddingOptions.SectionName}:ColbertEmbedModel"] = "colbert-ir/colbertv2.0",
            [$"{EmbeddingOptions.SectionName}:RerankPrefetch"] = "100",
            [$"{EmbeddingOptions.SectionName}:RerankMaxQueryTokens"] = "0",
            [$"{EmbeddingOptions.SectionName}:RerankAdaptiveEnabled"] = "false",
            [$"{EmbeddingOptions.SectionName}:RerankAdaptiveGap"] = "0.02",
            [$"{EmbeddingOptions.SectionName}:MaxDenseTokens"] = "0",
            [$"{EmbeddingOptions.SectionName}:MaxSparseTokens"] = "0",
            [$"{EmbeddingOptions.SectionName}:CachePath"] = "/root/.cache/fastembed",
            [$"{EmbeddingOptions.SectionName}:SparseThreads"] = "2",
            [$"{EmbeddingOptions.SectionName}:PrefetchMultiplier"] = "5",
            [$"{EmbeddingOptions.SectionName}:RrfK"] = "60",
            [$"{ColbertOptions.SectionName}:EmbedModel"] = "colbert-ir/colbertv2.0",
            [$"{ColbertOptions.SectionName}:EmbedBackend"] = "onnx",
            [$"{ColbertOptions.SectionName}:Url"] = "http://localhost:8082",
            [$"{ColbertOptions.SectionName}:TimeoutSeconds"] = "300",
            [$"{ColbertOptions.SectionName}:EmbedBatchSize"] = "16",
            [$"{ColbertOptions.SectionName}:UseCuda"] = "false",
            [$"{ReindexOptions.SectionName}:Enabled"] = "false",
            [$"{ReindexOptions.SectionName}:Cron"] = "0 3 * * *",
            [$"{ReindexOptions.SectionName}:Interval"] = "",
            [$"{ReindexOptions.SectionName}:GitPull"] = "false",
            [$"{ReindexOptions.SectionName}:IndexTimeoutSeconds"] = "1800",
            [$"{ReindexOptions.SectionName}:GitTimeoutSeconds"] = "120",
            [$"{TeiOptions.SectionName}:Url"] = "http://localhost:8080",
            [$"{TeiOptions.SectionName}:EmbedBatchSize"] = "32",
            [$"{TeiOptions.SectionName}:TimeoutSeconds"] = "120",
            [$"{TeiOptions.SectionName}:QueryInstruction"] = string.Empty,
            [$"{TeiOptions.SectionName}:NormalizeOutput"] = "false",
            [$"{WorkspaceOptions.SectionName}:Path"] = "/workspace",
            [$"{WorkspaceOptions.SectionName}:ExcludedDirs"] = "node_modules,.git",
            [$"{WorkspaceOptions.SectionName}:HashWorkerDop"] = "1",
            [$"{WorkspaceOptions.SectionName}:ReadaheadBuffer"] = "100",
            [$"{ChunkingOptions.SectionName}:MaxLines"] = "150",
            [$"{ChunkingOptions.SectionName}:OverlapLines"] = "20",
            [$"{IndexingOptions.SectionName}:SequentialEmbed"] = "false",
            [$"{IndexingOptions.SectionName}:MemoryPressureWarnPct"] = "70",
            [$"{IndexingOptions.SectionName}:MemoryPressureHaltPct"] = "85",
            [$"{IndexingOptions.SectionName}:ReleaseModelsAfterIndex"] = "true",
            [$"{IndexingOptions.SectionName}:FlushEvery"] = "1500",
            [$"{IndexingOptions.SectionName}:UpsertBatch"] = "500",
            [$"{IndexingOptions.SectionName}:BatchSize"] = "32",
            [$"{IndexingOptions.SectionName}:PreloadModels"] = "false",
            [$"{IndexingOptions.SectionName}:ModelIdleTimeoutSeconds"] = "300",
            [$"{DiscoveryOptions.SectionName}:RecommendEnabled"] = "true",
            [$"{DiscoveryOptions.SectionName}:RecommendMaxExamples"] = "10",
            [$"{DiscoveryOptions.SectionName}:OutlierMaxContextSamples"] = "200",
            [$"{DiscoveryOptions.SectionName}:OutlierMaxSimilarity"] = "0.55",
            [$"{DiscoveryOptions.SectionName}:ServiceUrlKeywords"] = DiscoveryOptions.DefaultServiceUrlKeywords,
            [$"{DiscoveryOptions.SectionName}:ServiceDiscoveryExtraQueries"] = "",
            [$"{GraphOptions.SectionName}:Enabled"] = "false",
            [$"{GraphOptions.SectionName}:Neo4jUri"] = "bolt://localhost:7687",
            [$"{GraphOptions.SectionName}:Neo4jUser"] = "neo4j",
            [$"{GraphOptions.SectionName}:Neo4jPassword"] = "",
            [$"{GraphOptions.SectionName}:Neo4jDatabase"] = "neo4j",
            [$"{GraphOptions.SectionName}:WriterBatch"] = "500",
            [$"{GraphOptions.SectionName}:MaxHops"] = "2",
            [$"{GraphOptions.SectionName}:MaxNodes"] = "200",
        };
}
