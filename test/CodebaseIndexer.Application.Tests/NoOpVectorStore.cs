using CodebaseIndexer.Domain.Models;
using CodebaseIndexer.Domain.Ports;

namespace CodebaseIndexer.Application.Tests;

/// <summary>Default no-op <see cref="IVectorStore"/> for unit tests.</summary>
internal class NoOpVectorStore : IVectorStore
{
    /// <inheritdoc />
    public virtual ValueTask<bool> CollectionExistsAsync(string collection, CancellationToken cancellationToken = default) =>
        ValueTask.FromResult(true);

    /// <inheritdoc />
    public virtual Task EnsureCollectionAsync(string collection, bool force = false, CancellationToken cancellationToken = default) =>
        Task.CompletedTask;

    /// <inheritdoc />
    public virtual Task UpsertChunksAsync(
        string collection,
        IReadOnlyList<EmbeddedChunk> chunks,
        bool omitCallees = false,
        IReadOnlyDictionary<string, IReadOnlyList<string>>? graphNodeIdsByChunk = null,
        CancellationToken cancellationToken = default) =>
        Task.CompletedTask;

    /// <inheritdoc />
    public virtual Task SetCollectionGraphCallSitesAsync(
        string collection,
        bool enabled = true,
        CancellationToken cancellationToken = default) =>
        Task.CompletedTask;

    /// <inheritdoc />
    public virtual Task SetCollectionGraphEnabledAsync(
        string collection,
        bool enabled = true,
        CancellationToken cancellationToken = default) =>
        Task.CompletedTask;

    /// <inheritdoc />
    public virtual ValueTask<bool> CollectionHasGraphCallSitesAsync(
        string collection,
        CancellationToken cancellationToken = default) =>
        ValueTask.FromResult(false);

    /// <inheritdoc />
    public virtual ValueTask<bool> CollectionHasGraphEnabledAsync(
        string collection,
        CancellationToken cancellationToken = default) =>
        ValueTask.FromResult(false);

    /// <inheritdoc />
    public virtual Task<IReadOnlyList<SearchHit>> SearchAsync(
        string collection,
        IReadOnlyList<float> denseVector,
        SparseVector? sparseVector,
        int topK,
        SourceLanguage? language = null,
        float minScore = 0.5f,
        IReadOnlyList<IReadOnlyList<float>>? colbertVector = null,
        CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<SearchHit>>([]);

    /// <inheritdoc />
    public virtual Task<ChunkPayload?> GetChunkByIdAsync(
        string collection,
        string chunkId,
        CancellationToken cancellationToken = default) =>
        Task.FromResult<ChunkPayload?>(null);

    /// <inheritdoc />
    public virtual Task<ChunkPayload?> FindChunkByIdAsync(
        string chunkId,
        string? collection = null,
        CancellationToken cancellationToken = default) =>
        Task.FromResult<ChunkPayload?>(null);

    /// <inheritdoc />
    public virtual Task<IReadOnlyList<FileSymbol>> ScrollFileSymbolsAsync(
        string collection,
        string relPath,
        CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<FileSymbol>>([]);

    /// <inheritdoc />
    public virtual Task<IReadOnlyList<PayloadRow>> ScrollAllPayloadsAsync(
        string collection,
        CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<PayloadRow>>([]);

    /// <inheritdoc />
    public virtual Task<IReadOnlyList<CollectionStats>> ListCollectionStatsAsync(
        CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<CollectionStats>>([]);

    /// <inheritdoc />
    public virtual Task<IReadOnlyList<SearchHit>> FindSymbolInCollectionsAsync(
        string symbolName,
        IReadOnlyList<string> collections,
        int limitPerCollection = 10,
        CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<SearchHit>>([]);

    /// <inheritdoc />
    public virtual Task<IReadOnlyList<string>> ListCollectionsAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<string>>([]);

    /// <inheritdoc />
    public virtual ValueTask<CollectionStats?> GetCollectionStatsAsync(
        string collection,
        CancellationToken cancellationToken = default) =>
        ValueTask.FromResult<CollectionStats?>(null);

    /// <inheritdoc />
    public virtual Task<IReadOnlyDictionary<string, FileMetadata>> GetFileMetadataAsync(
        string collection,
        CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyDictionary<string, FileMetadata>>(new Dictionary<string, FileMetadata>());

    /// <inheritdoc />
    public virtual Task DeleteByPathsAsync(
        string collection,
        IReadOnlyList<string> relPaths,
        CancellationToken cancellationToken = default) =>
        Task.CompletedTask;

    /// <inheritdoc />
    public virtual Task SetIndexingAsync(
        string collection,
        bool enabled,
        CancellationToken cancellationToken = default) =>
        Task.CompletedTask;

    /// <inheritdoc />
    public virtual Task VerifyChunkIdsExistAsync(
        string collection,
        IReadOnlyList<string> chunkIds,
        CancellationToken cancellationToken = default) =>
        Task.CompletedTask;

    /// <inheritdoc />
    public virtual Task<IReadOnlyList<SearchHit>> RecommendAsync(
        string collection,
        IReadOnlyList<RecommendExample> positive,
        IReadOnlyList<RecommendExample>? negative = null,
        int limit = 5,
        SourceLanguage? language = null,
        string? pathGlob = null,
        CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<SearchHit>>([]);

    /// <inheritdoc />
    public virtual Task<IReadOnlyList<SearchHit>> FindOutlierChunksAsync(
        string collection,
        IReadOnlyList<string>? contextChunkIds = null,
        int limit = 5,
        SourceLanguage? language = null,
        string? pathGlob = null,
        float? maxSimilarity = null,
        int? maxContextSamples = null,
        CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<SearchHit>>([]);

    /// <inheritdoc />
    public virtual Task<IReadOnlyList<SearchHit>> FindCallersInCollectionsAsync(
        string method,
        IReadOnlyList<string> collections,
        string? receiver = null,
        int limitPerCollection = 10,
        CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<SearchHit>>([]);

    /// <inheritdoc />
    public virtual Task<IReadOnlyList<IReadOnlyDictionary<string, string>>> ScrollChunksByPathsAsync(
        string collection,
        IReadOnlyList<string> relPaths,
        IReadOnlyList<string>? payloadFields = null,
        int limit = 500,
        CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<IReadOnlyDictionary<string, string>>>([]);
}
