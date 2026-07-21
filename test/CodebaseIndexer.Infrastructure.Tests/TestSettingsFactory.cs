using CodebaseIndexer.Application.Options;
using CodebaseIndexer.Infrastructure.Configuration;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;

namespace CodebaseIndexer.Infrastructure.Tests;

internal static class TestSettingsFactory
{
    public static IOptions<KnownEmbedModelsOptions> CreateKnownEmbedModelsOptions() =>
        Options.Create(CreateKnownEmbedModels());

    public static KnownEmbedModelsOptions CreateKnownEmbedModels() =>
        KnownEmbedModelsFactory.Create(new ConfigurationBuilder().Build());

    public static QdrantOptions CreateQdrantOptions(string? url = null) => new()
    {
        Url = url ?? "http://localhost:6334",
        TimeoutSeconds = 30,
        Collection = "codebase",
        PayloadIndexes = true,
        VectorsOnDisk = false,
        SparseOnDisk = false,
        Quantization = true,
        HnswEf = 64,
        HnswM = 16,
        HnswEfConstruct = 128,
        QuantOversampling = 2.0,
        MemmapThresholdKb = 20_000,
    };

    public static EmbeddingOptions CreateEmbeddingOptions(int? denseVectorSize = null) => new()
    {
        HybridSearch = true,
        DenseModel = "test-model",
        SparseModel = "Qdrant/bm25",
        DenseVectorSize = denseVectorSize ?? 768,
        RerankEnabled = false,
        MaxDenseTokens = 0,
        MaxSparseTokens = 0,
        CachePath = "/root/.cache/fastembed",
        SparseThreads = 2,
        PrefetchMultiplier = 5,
        RrfK = 60,
    };

    public static TeiOptions CreateTeiOptions(string? url = null) => new()
    {
        Url = url ?? "http://localhost:8080",
        EmbedBatchSize = 32,
        TimeoutSeconds = 120,
        QueryInstruction = string.Empty,
        NormalizeOutput = false,
    };

    public static WorkspaceOptions CreateWorkspaceOptions() => new()
    {
        Path = "/workspace",
        ExcludedDirs = "node_modules,.git",
        HashWorkerDop = 1,
        ReadaheadBuffer = 100,
    };

    public static ChunkingOptions CreateChunkingOptions() => new()
    {
        MaxLines = 150,
        OverlapLines = 20,
    };

    public static IndexingOptions CreateIndexingOptions() => new()
    {
        SequentialEmbed = false,
        MemoryPressureWarnPct = 70,
        MemoryPressureHaltPct = 85,
        ReleaseModelsAfterIndex = true,
        FlushEvery = 1500,
        UpsertBatch = 500,
        BatchSize = 32,
        PreloadModels = true,
        ModelIdleTimeoutSeconds = 300,
    };

    public static Dictionary<string, string?> CreateConfigurationValues(
        string? qdrantUrl = null,
        string? teiUrl = null,
        int? denseVectorSize = null) =>
        ToConfigurationValues(
            CreateQdrantOptions(qdrantUrl),
            CreateEmbeddingOptions(denseVectorSize),
            CreateTeiOptions(teiUrl),
            CreateWorkspaceOptions(),
            CreateChunkingOptions(),
            CreateIndexingOptions());

    public static IConfiguration CreateConfiguration(
        string? qdrantUrl = null,
        string? teiUrl = null,
        int? denseVectorSize = null) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(CreateConfigurationValues(qdrantUrl, teiUrl, denseVectorSize))
            .Build();

    private static Dictionary<string, string?> ToConfigurationValues(
        QdrantOptions qdrant,
        EmbeddingOptions embedding,
        TeiOptions tei,
        WorkspaceOptions workspace,
        ChunkingOptions chunking,
        IndexingOptions indexing)
    {
        return new Dictionary<string, string?>
        {
            [$"{QdrantOptions.SectionName}:Url"] = qdrant.Url,
            [$"{QdrantOptions.SectionName}:TimeoutSeconds"] = qdrant.TimeoutSeconds.ToString(),
            [$"{QdrantOptions.SectionName}:Collection"] = qdrant.Collection,
            [$"{QdrantOptions.SectionName}:PayloadIndexes"] = qdrant.PayloadIndexes.ToString().ToLowerInvariant(),
            [$"{QdrantOptions.SectionName}:VectorsOnDisk"] = qdrant.VectorsOnDisk.ToString().ToLowerInvariant(),
            [$"{QdrantOptions.SectionName}:SparseOnDisk"] = qdrant.SparseOnDisk.ToString().ToLowerInvariant(),
            [$"{QdrantOptions.SectionName}:Quantization"] = qdrant.Quantization.ToString().ToLowerInvariant(),
            [$"{QdrantOptions.SectionName}:HnswEf"] = qdrant.HnswEf.ToString(),
            [$"{QdrantOptions.SectionName}:HnswM"] = qdrant.HnswM.ToString(),
            [$"{QdrantOptions.SectionName}:HnswEfConstruct"] = qdrant.HnswEfConstruct.ToString(),
            [$"{QdrantOptions.SectionName}:QuantOversampling"] = qdrant.QuantOversampling.ToString(System.Globalization.CultureInfo.InvariantCulture),
            [$"{QdrantOptions.SectionName}:MemmapThresholdKb"] = qdrant.MemmapThresholdKb.ToString(),
            [$"{EmbeddingOptions.SectionName}:HybridSearch"] = embedding.HybridSearch.ToString().ToLowerInvariant(),
            [$"{EmbeddingOptions.SectionName}:DenseModel"] = embedding.DenseModel,
            [$"{EmbeddingOptions.SectionName}:SparseModel"] = embedding.SparseModel,
            [$"{EmbeddingOptions.SectionName}:DenseVectorSize"] = embedding.DenseVectorSize.ToString(),
            [$"{EmbeddingOptions.SectionName}:RerankEnabled"] = embedding.RerankEnabled.ToString().ToLowerInvariant(),
            [$"{EmbeddingOptions.SectionName}:MaxDenseTokens"] = embedding.MaxDenseTokens.ToString(),
            [$"{EmbeddingOptions.SectionName}:MaxSparseTokens"] = embedding.MaxSparseTokens.ToString(),
            [$"{EmbeddingOptions.SectionName}:CachePath"] = embedding.CachePath,
            [$"{EmbeddingOptions.SectionName}:SparseThreads"] = embedding.SparseThreads.ToString(),
            [$"{EmbeddingOptions.SectionName}:PrefetchMultiplier"] = embedding.PrefetchMultiplier.ToString(),
            [$"{EmbeddingOptions.SectionName}:RrfK"] = embedding.RrfK.ToString(),
            [$"{TeiOptions.SectionName}:Url"] = tei.Url,
            [$"{TeiOptions.SectionName}:EmbedBatchSize"] = tei.EmbedBatchSize.ToString(),
            [$"{TeiOptions.SectionName}:TimeoutSeconds"] = tei.TimeoutSeconds.ToString(),
            [$"{TeiOptions.SectionName}:QueryInstruction"] = tei.QueryInstruction,
            [$"{TeiOptions.SectionName}:NormalizeOutput"] = tei.NormalizeOutput.ToString().ToLowerInvariant(),
            [$"{WorkspaceOptions.SectionName}:Path"] = workspace.Path,
            [$"{WorkspaceOptions.SectionName}:ExcludedDirs"] = workspace.ExcludedDirs,
            [$"{WorkspaceOptions.SectionName}:HashWorkerDop"] = workspace.HashWorkerDop.ToString(),
            [$"{WorkspaceOptions.SectionName}:ReadaheadBuffer"] = workspace.ReadaheadBuffer.ToString(),
            [$"{ChunkingOptions.SectionName}:MaxLines"] = chunking.MaxLines.ToString(),
            [$"{ChunkingOptions.SectionName}:OverlapLines"] = chunking.OverlapLines.ToString(),
            [$"{IndexingOptions.SectionName}:SequentialEmbed"] = indexing.SequentialEmbed.ToString().ToLowerInvariant(),
            [$"{IndexingOptions.SectionName}:MemoryPressureWarnPct"] = indexing.MemoryPressureWarnPct.ToString(),
            [$"{IndexingOptions.SectionName}:MemoryPressureHaltPct"] = indexing.MemoryPressureHaltPct.ToString(),
            [$"{IndexingOptions.SectionName}:ReleaseModelsAfterIndex"] = indexing.ReleaseModelsAfterIndex.ToString().ToLowerInvariant(),
            [$"{IndexingOptions.SectionName}:FlushEvery"] = indexing.FlushEvery.ToString(),
            [$"{IndexingOptions.SectionName}:UpsertBatch"] = indexing.UpsertBatch.ToString(),
            [$"{IndexingOptions.SectionName}:BatchSize"] = indexing.BatchSize.ToString(),
            [$"{IndexingOptions.SectionName}:PreloadModels"] = indexing.PreloadModels.ToString().ToLowerInvariant(),
            [$"{IndexingOptions.SectionName}:ModelIdleTimeoutSeconds"] = indexing.ModelIdleTimeoutSeconds.ToString(),
        };
    }
}
