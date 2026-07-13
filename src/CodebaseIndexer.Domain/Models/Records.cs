namespace CodebaseIndexer.Domain.Models;

public enum IndexJobStatus
{
    Queued,
    Running,
    Done,
    Failed,
    Cancelled,
}

public readonly record struct ChunkId(string Value);

public static class ChunkIdFactory
{
    public static ChunkId FromPathAndLine(string relPath, int startLine) =>
        new(Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes($"{relPath}:{startLine}"))).ToLowerInvariant());
}

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

public sealed record FileRecord(
    string AbsPath,
    string RelPath,
    string Language,
    string Content,
    string Sha256Hash,
    double Mtime = 0,
    bool MtimeSkipped = false);

public sealed record FileMetadata(string Sha256, double? Mtime);

public sealed record PipelineResult(
    int TotalFiles = 0,
    int IndexedFiles = 0,
    int SkippedFiles = 0,
    int TotalChunks = 0,
    double ElapsedSeconds = 0,
    IReadOnlyList<string> Errors = null!)
{
    public IReadOnlyList<string> Errors { get; init; } = Errors ?? Array.Empty<string>();
}

public sealed record IndexJobSnapshot(
    string Collection,
    string Path,
    IndexJobStatus Status,
    double ElapsedSeconds,
    int TotalFiles,
    int IndexedFiles,
    int SkippedFiles,
    int TotalChunks,
    IReadOnlyList<string> Errors,
    string ErrorMessage = "");

public sealed record IndexCodebaseCommand(
    string Collection,
    string Path,
    bool Force = false,
    bool Wait = true,
    int TimeoutSeconds = 1800);

public sealed record IndexAllCommand(
    bool Force = false,
    bool Wait = true,
    int TimeoutSeconds = 1800);
