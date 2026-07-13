using CodebaseIndexer.Infrastructure.Configuration;

namespace CodebaseIndexer.Infrastructure.Tests;

internal static class TestSettingsFactory
{
    public static Settings Create(
        string? qdrantUrl = null,
        string? teiUrl = null,
        int? denseEmbedVectorSize = null) => new()
    {
        QdrantUrl = qdrantUrl ?? "http://localhost:6333",
        QdrantTimeoutSeconds = 30,
        QdrantCollection = "codebase",
        HybridSearch = true,
        DenseEmbedModel = "test-model",
        SparseEmbedModel = "Qdrant/bm25",
        DenseEmbedVectorSize = denseEmbedVectorSize ?? 768,
        TeiUrl = teiUrl ?? "http://localhost:8080",
        TeiEmbedBatchSize = 32,
        TeiTimeoutSeconds = 120,
        QueryInstruction = string.Empty,
        NormalizeOutput = false,
        RerankEnabled = false,
        PayloadIndexes = true,
        VectorsOnDisk = false,
        SparseOnDisk = false,
        WorkspacePath = "/workspace",
        MaxChunkLines = 150,
        ChunkOverlapLines = 20,
        BatchSize = 32,
        FlushEvery = 1500,
        UpsertBatch = 500,
        ReadaheadBuffer = 100,
        HashWorkerDop = 1,
        MaxDenseEmbedTokens = 0,
        MaxSparseEmbedTokens = 0,
        SparseThreads = 2,
        SequentialEmbed = false,
        MemoryPressureWarnPct = 70,
        MemoryPressureHaltPct = 85,
        ReleaseModelsAfterIndex = true,
        ModelIdleTimeoutSeconds = 300,
        PreloadModels = true,
        FastembedCachePath = "/root/.cache/fastembed",
        ExcludedDirs = "node_modules,.git",
    };
}
