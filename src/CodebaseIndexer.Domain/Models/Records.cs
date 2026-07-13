namespace CodebaseIndexer.Domain.Models;

public readonly record struct ChunkId(string Value);

public sealed record SparseVector(IReadOnlyList<uint> Indices, IReadOnlyList<float> Values);

public sealed record Chunk(
    ChunkId Id,
    string RelPath,
    string Content,
    int StartLine,
    int EndLine,
    string? SymbolName,
    string Language,
    string FileSha256);

public sealed record EmbeddedChunk(
    Chunk Chunk,
    IReadOnlyList<float> DenseVector,
    SparseVector? SparseVector);

public sealed record CollectionStats(
    string Name,
    long VectorCount,
    double DiskSizeMb,
    string DenseEmbedModel,
    string SparseEmbedModel,
    string DenseEmbedBackend,
    bool Hybrid,
    bool RerankEnabled = false,
    string ColbertEmbedModel = "",
    bool GraphCallSites = false,
    bool GraphEnabled = false);

public sealed record SearchHit(
    ChunkId Id,
    double Score,
    string RelPath,
    string Language,
    int StartLine,
    int EndLine,
    string? SymbolName,
    string SymbolType,
    string Content,
    string Collection = "");
