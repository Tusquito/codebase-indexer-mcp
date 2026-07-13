using Microsoft.Extensions.Configuration;

namespace CodebaseIndexer.Infrastructure.Configuration;

public sealed class Settings
{
    public string QdrantUrl { get; init; } = "http://localhost:6333";
    public int QdrantTimeoutSeconds { get; init; } = 30;
    public string QdrantCollection { get; init; } = "codebase";
    public bool HybridSearch { get; init; } = true;
    public string DenseEmbedModel { get; init; } = "jinaai/jina-embeddings-v2-base-code";
    public string SparseEmbedModel { get; init; } = "Qdrant/bm25";
    public int DenseEmbedVectorSize { get; init; } = 768;
    public string TeiUrl { get; init; } = "http://localhost:8080";
    public int TeiEmbedBatchSize { get; init; } = 32;
    public int TeiTimeoutSeconds { get; init; } = 120;
    public int? MrlDimensions { get; init; }
    public string QueryInstruction { get; init; } = "";
    public bool NormalizeOutput { get; init; }
    public bool RerankEnabled { get; init; }
    public bool PayloadIndexes { get; init; } = true;
    public bool VectorsOnDisk { get; init; }
    public bool SparseOnDisk { get; init; }

    public static Settings FromConfiguration(IConfiguration configuration)
    {
        var settings = new Settings();
        configuration.Bind(settings);

        return new Settings
        {
            QdrantUrl = configuration.GetConnectionString("qdrant")
                ?? GetEnv("QDRANT_URL", settings.QdrantUrl),
            QdrantTimeoutSeconds = GetEnvInt("QDRANT_TIMEOUT", settings.QdrantTimeoutSeconds),
            QdrantCollection = GetEnv("QDRANT_COLLECTION", settings.QdrantCollection),
            HybridSearch = GetEnvBool("HYBRID_SEARCH", settings.HybridSearch),
            DenseEmbedModel = GetEnv("DENSE_EMBED_MODEL", settings.DenseEmbedModel),
            SparseEmbedModel = GetEnv("SPARSE_EMBED_MODEL", settings.SparseEmbedModel),
            DenseEmbedVectorSize = GetEnvInt("DENSE_EMBED_VECTOR_SIZE", settings.DenseEmbedVectorSize),
            TeiUrl = configuration.GetConnectionString("tei")
                ?? GetEnv("TEI_URL", settings.TeiUrl),
            TeiEmbedBatchSize = GetEnvInt("TEI_EMBED_BATCH_SIZE", settings.TeiEmbedBatchSize),
            TeiTimeoutSeconds = GetEnvInt("TEI_TIMEOUT", settings.TeiTimeoutSeconds),
            MrlDimensions = GetEnvNullableInt("MRL_DIMENSIONS") ?? settings.MrlDimensions,
            QueryInstruction = GetEnv("QUERY_INSTRUCTION", settings.QueryInstruction),
            NormalizeOutput = GetEnvBool("NORMALIZE_OUTPUT", settings.NormalizeOutput),
            RerankEnabled = GetEnvBool("RERANK_ENABLED", settings.RerankEnabled),
            PayloadIndexes = GetEnvBool("PAYLOAD_INDEXES", settings.PayloadIndexes),
            VectorsOnDisk = GetEnvBool("VECTORS_ON_DISK", settings.VectorsOnDisk),
            SparseOnDisk = GetEnvBool("SPARSE_ON_DISK", settings.SparseOnDisk),
        };
    }

    private static string GetEnv(string key, string fallback) =>
        Environment.GetEnvironmentVariable(key) is { Length: > 0 } value ? value : fallback;

    private static int GetEnvInt(string key, int fallback) =>
        int.TryParse(Environment.GetEnvironmentVariable(key), out var value) ? value : fallback;

    private static bool GetEnvBool(string key, bool fallback) =>
        bool.TryParse(Environment.GetEnvironmentVariable(key), out var value) ? value : fallback;

    private static int? GetEnvNullableInt(string key) =>
        int.TryParse(Environment.GetEnvironmentVariable(key), out var value) ? value : null;
}
