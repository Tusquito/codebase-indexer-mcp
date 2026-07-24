using CodebaseIndexer.Domain.Models;
using CodebaseIndexer.Domain.Ports;
using CodebaseIndexer.Domain.Results;

namespace CodebaseIndexer.Application.Tests;

/// <summary>Default no-op <see cref="IVectorStore"/> for unit tests.</summary>
internal class NoOpVectorStore : IVectorStore
{
    /// <inheritdoc />
    public virtual ValueTask<bool> CollectionExistsAsync(string collection, CancellationToken cancellationToken = default) =>
        ValueTask.FromResult(true);

    /// <inheritdoc />
    public virtual Task<Result> EnsureCollectionAsync(string collection, bool force = false, CancellationToken cancellationToken = default) =>
        Task.FromResult(Result.Success());

    /// <inheritdoc />
    public virtual Task<Result> UpsertChunksAsync(
        string collection,
        IReadOnlyList<EmbeddedChunk> chunks,
        bool omitCallees = false,
        IReadOnlyDictionary<string, IReadOnlyList<string>>? graphNodeIdsByChunk = null,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(Result.Success());

    /// <inheritdoc />
    public virtual Task<Result> SetCollectionGraphCallSitesAsync(
        string collection,
        bool enabled = true,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(Result.Success());

    /// <inheritdoc />
    public virtual Task<Result> SetCollectionGraphEnabledAsync(
        string collection,
        bool enabled = true,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(Result.Success());

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
    public virtual Task<Result<IReadOnlyList<SearchHit>>> SearchAsync(
        string collection,
        IReadOnlyList<float> denseVector,
        SparseVector? sparseVector,
        int topK,
        SourceLanguage? language = null,
        float minScore = 0.5f,
        IReadOnlyList<IReadOnlyList<float>>? colbertVector = null,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(Result<IReadOnlyList<SearchHit>>.Success([]));

    /// <inheritdoc />
    public virtual Task<Result<ChunkPayload>> GetChunkByIdAsync(
        string collection,
        string chunkId,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(Result<ChunkPayload>.Failure(new Error(
            ErrorKind.NotFound,
            StoreErrorCodes.ChunkNotFound,
            $"Chunk '{chunkId}' not found.")));

    /// <inheritdoc />
    public virtual Task<Result<ChunkPayload>> FindChunkByIdAsync(
        string chunkId,
        string? collection = null,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(Result<ChunkPayload>.Failure(new Error(
            ErrorKind.NotFound,
            StoreErrorCodes.ChunkNotFound,
            $"Chunk '{chunkId}' not found.")));

    /// <inheritdoc />
    public virtual Task<Result<IReadOnlyList<FileSymbol>>> ScrollFileSymbolsAsync(
        string collection,
        string relPath,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(Result<IReadOnlyList<FileSymbol>>.Success([]));

    /// <inheritdoc />
    public virtual Task<Result<IReadOnlyList<PayloadRow>>> ScrollAllPayloadsAsync(
        string collection,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(Result<IReadOnlyList<PayloadRow>>.Success([]));

    /// <inheritdoc />
    public virtual Task<Result<IReadOnlyList<CollectionStats>>> ListCollectionStatsAsync(
        CancellationToken cancellationToken = default) =>
        Task.FromResult(Result<IReadOnlyList<CollectionStats>>.Success([]));

    /// <inheritdoc />
    public virtual Task<Result<IReadOnlyList<SearchHit>>> FindSymbolInCollectionsAsync(
        string symbolName,
        IReadOnlyList<string> collections,
        int limitPerCollection = 10,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(Result<IReadOnlyList<SearchHit>>.Success([]));

    /// <inheritdoc />
    public virtual Task<Result<IReadOnlyList<string>>> ListCollectionsAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(Result<IReadOnlyList<string>>.Success([]));

    /// <inheritdoc />
    public virtual ValueTask<Result<CollectionStats>> GetCollectionStatsAsync(
        string collection,
        CancellationToken cancellationToken = default) =>
        ValueTask.FromResult(Result<CollectionStats>.Failure(new Error(
            ErrorKind.NotFound,
            StoreErrorCodes.CollectionNotFound,
            $"Collection '{collection}' not found.")));

    /// <inheritdoc />
    public virtual Task<Result<IReadOnlyDictionary<string, FileMetadata>>> GetFileMetadataAsync(
        string collection,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(Result<IReadOnlyDictionary<string, FileMetadata>>.Success(
            new Dictionary<string, FileMetadata>()));

    /// <inheritdoc />
    public virtual Task<Result> DeleteByPathsAsync(
        string collection,
        IReadOnlyList<string> relPaths,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(Result.Success());

    /// <inheritdoc />
    public virtual Task<Result> SetIndexingAsync(
        string collection,
        bool enabled,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(Result.Success());

    /// <inheritdoc />
    public virtual Task<Result> VerifyChunkIdsExistAsync(
        string collection,
        IReadOnlyList<string> chunkIds,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(Result.Success());

    /// <inheritdoc />
    public virtual Task<Result<IReadOnlyList<SearchHit>>> RecommendAsync(
        string collection,
        IReadOnlyList<RecommendExample> positive,
        IReadOnlyList<RecommendExample>? negative = null,
        int limit = 5,
        SourceLanguage? language = null,
        string? pathGlob = null,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(Result<IReadOnlyList<SearchHit>>.Success([]));

    /// <inheritdoc />
    public virtual Task<Result<IReadOnlyList<SearchHit>>> FindOutlierChunksAsync(
        string collection,
        IReadOnlyList<string>? contextChunkIds = null,
        int limit = 5,
        SourceLanguage? language = null,
        string? pathGlob = null,
        float? maxSimilarity = null,
        int? maxContextSamples = null,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(Result<IReadOnlyList<SearchHit>>.Success([]));

    /// <inheritdoc />
    public virtual Task<Result<IReadOnlyList<SearchHit>>> FindCallersInCollectionsAsync(
        string method,
        IReadOnlyList<string> collections,
        string? receiver = null,
        int limitPerCollection = 10,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(Result<IReadOnlyList<SearchHit>>.Success([]));

    /// <inheritdoc />
    public virtual Task<Result<IReadOnlyList<IReadOnlyDictionary<string, string>>>> ScrollChunksByPathsAsync(
        string collection,
        IReadOnlyList<string> relPaths,
        IReadOnlyList<string>? payloadFields = null,
        int limit = 500,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(Result<IReadOnlyList<IReadOnlyDictionary<string, string>>>.Success([]));
}
