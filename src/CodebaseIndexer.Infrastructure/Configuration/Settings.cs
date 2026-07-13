namespace CodebaseIndexer.Infrastructure.Configuration;

public sealed class Settings
{
    public const string SectionName = "CodebaseIndexer";

    public required string QdrantUrl { get; init; }
    public required int QdrantTimeoutSeconds { get; init; }
    public required string QdrantCollection { get; init; }
    public required bool HybridSearch { get; init; }
    public required string DenseEmbedModel { get; init; }
    public required string SparseEmbedModel { get; init; }
    public required int DenseEmbedVectorSize { get; init; }
    public required string TeiUrl { get; init; }
    public required int TeiEmbedBatchSize { get; init; }
    public required int TeiTimeoutSeconds { get; init; }
    public int? MrlDimensions { get; init; }
    public required string QueryInstruction { get; init; }
    public required bool NormalizeOutput { get; init; }
    public required bool RerankEnabled { get; init; }
    public required bool PayloadIndexes { get; init; }
    public required bool VectorsOnDisk { get; init; }
    public required bool SparseOnDisk { get; init; }
}
