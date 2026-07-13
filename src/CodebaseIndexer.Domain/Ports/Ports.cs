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
}
