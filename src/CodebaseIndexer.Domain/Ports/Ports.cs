using CodebaseIndexer.Domain.Models;

namespace CodebaseIndexer.Domain.Ports;

public interface IDenseEmbedder
{
    string BackendName { get; }
    int VectorSize { get; }
    bool IsLoaded { get; }

    Task PreloadAsync(CancellationToken cancellationToken = default);
    void Release();
    Task<IReadOnlyList<IReadOnlyList<float>>> EmbedBatchAsync(
        IReadOnlyList<string> texts,
        CancellationToken cancellationToken = default);
    Task<IReadOnlyList<IReadOnlyList<float>>> EmbedQueryAsync(
        IReadOnlyList<string> texts,
        CancellationToken cancellationToken = default);
}

public interface ISparseEmbedder
{
    string BackendName { get; }
    bool IsLoaded { get; }

    Task PreloadAsync(CancellationToken cancellationToken = default);
    void Release();
    Task<IReadOnlyList<SparseVector>> EmbedBatchAsync(
        IReadOnlyList<string> texts,
        CancellationToken cancellationToken = default);
}

public interface ICodeChunker
{
    IReadOnlyList<Chunk> ChunkFile(string relPath, string content, string language, string fileSha256);
}

public interface IGraphStore
{
    ValueTask<bool> IsEnabledAsync(CancellationToken cancellationToken = default);
}

public interface IVectorStore
{
    ValueTask<bool> CollectionExistsAsync(string collection, CancellationToken cancellationToken = default);
    Task EnsureCollectionAsync(string collection, bool force = false, CancellationToken cancellationToken = default);
    Task UpsertChunksAsync(
        string collection,
        IReadOnlyList<EmbeddedChunk> chunks,
        CancellationToken cancellationToken = default);
    Task<IReadOnlyList<SearchHit>> SearchAsync(
        string collection,
        string query,
        int limit,
        CancellationToken cancellationToken = default);
    Task<IReadOnlyList<string>> ListCollectionsAsync(CancellationToken cancellationToken = default);
    ValueTask<CollectionStats?> GetCollectionStatsAsync(
        string collection,
        CancellationToken cancellationToken = default);
    Task<IReadOnlyDictionary<string, FileMetadata>> GetFileMetadataAsync(
        string collection,
        CancellationToken cancellationToken = default);
    Task DeleteByPathsAsync(
        string collection,
        IReadOnlyList<string> relPaths,
        CancellationToken cancellationToken = default);
    Task SetIndexingAsync(
        string collection,
        bool enabled,
        CancellationToken cancellationToken = default);
}

public interface IWorkspaceScanner
{
    IAsyncEnumerable<FileRecord> ScanFilesAsync(
        string workspacePath,
        string subPath,
        IReadOnlyDictionary<string, FileMetadata>? existingMetadata,
        bool force,
        CancellationToken cancellationToken = default);
}

public interface IIndexPipeline
{
    Task<PipelineResult> RunAsync(
        string collection,
        string subPath,
        bool force,
        CancellationToken cancellationToken);
}

public enum MemoryPressureSeverity
{
    Ok,
    Warn,
    Halt,
}

public interface IMemoryPressureGuard
{
    (MemoryPressureSeverity Severity, double Percent) Check(int warnPct, int haltPct);
}
