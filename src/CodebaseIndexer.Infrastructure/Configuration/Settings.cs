namespace CodebaseIndexer.Infrastructure.Configuration;

public sealed class Settings
{
    public const string SectionName = "CodebaseIndexer";

    public string QdrantUrl { get; set; } = string.Empty;
    public int QdrantTimeoutSeconds { get; set; }
    public string QdrantCollection { get; set; } = string.Empty;
    public bool HybridSearch { get; set; }
    public string DenseEmbedModel { get; set; } = string.Empty;
    public string SparseEmbedModel { get; set; } = string.Empty;
    public int DenseEmbedVectorSize { get; set; }
    public string TeiUrl { get; set; } = string.Empty;
    public int TeiEmbedBatchSize { get; set; }
    public int TeiTimeoutSeconds { get; set; }
    public int? MrlDimensions { get; set; }
    public string QueryInstruction { get; set; } = string.Empty;
    public bool NormalizeOutput { get; set; }
    public bool RerankEnabled { get; set; }
    public bool PayloadIndexes { get; set; }
    public bool VectorsOnDisk { get; set; }
    public bool SparseOnDisk { get; set; }

    public string WorkspacePath { get; set; } = string.Empty;
    public int MaxChunkLines { get; set; }
    public int ChunkOverlapLines { get; set; }
    public int BatchSize { get; set; }
    public int FlushEvery { get; set; }
    public int UpsertBatch { get; set; }
    public int ReadaheadBuffer { get; set; }
    public int HashWorkerDop { get; set; }
    public int MaxDenseEmbedTokens { get; set; }
    public int MaxSparseEmbedTokens { get; set; }
    public int SparseThreads { get; set; }
    public bool SequentialEmbed { get; set; }
    public int MemoryPressureWarnPct { get; set; }
    public int MemoryPressureHaltPct { get; set; }
    public bool ReleaseModelsAfterIndex { get; set; }
    public int ModelIdleTimeoutSeconds { get; set; }
    public bool PreloadModels { get; set; }
    public string FastembedCachePath { get; set; } = string.Empty;
    public string ExcludedDirs { get; set; } = string.Empty;
}
