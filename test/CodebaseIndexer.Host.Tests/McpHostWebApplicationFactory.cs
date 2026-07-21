using CodebaseIndexer.Application.Options;
using CodebaseIndexer.Infrastructure.Configuration;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;

namespace CodebaseIndexer.Host.Tests;

/// <summary>Web application factory with in-memory test configuration.</summary>
public sealed class McpHostWebApplicationFactory : WebApplicationFactory<Program>
{
    /// <inheritdoc />
    protected override void ConfigureWebHost(Microsoft.AspNetCore.Hosting.IWebHostBuilder builder)
    {
        builder.ConfigureAppConfiguration((_, config) =>
        {
            config.AddInMemoryCollection(new Dictionary<string, string?>(StringComparer.Ordinal)
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
                [$"{EmbeddingOptions.SectionName}:MaxDenseTokens"] = "0",
                [$"{EmbeddingOptions.SectionName}:MaxSparseTokens"] = "0",
                [$"{EmbeddingOptions.SectionName}:CachePath"] = "/root/.cache/fastembed",
                [$"{EmbeddingOptions.SectionName}:SparseThreads"] = "2",
                [$"{EmbeddingOptions.SectionName}:PrefetchMultiplier"] = "5",
                [$"{EmbeddingOptions.SectionName}:RrfK"] = "60",
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
            });
        });
    }
}
