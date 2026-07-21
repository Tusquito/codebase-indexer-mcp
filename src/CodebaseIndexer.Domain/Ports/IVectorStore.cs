using CodebaseIndexer.Domain.Models;

namespace CodebaseIndexer.Domain.Ports;

/// <summary>Port for persisting and querying embedded code chunks in a vector store.</summary>
public interface IVectorStore
{
    /// <summary>Checks whether a collection exists in the vector store.</summary>
    ValueTask<bool> CollectionExistsAsync(string collection, CancellationToken cancellationToken = default);

    /// <summary>Creates a collection if it does not exist, optionally recreating it when forced.</summary>
    Task EnsureCollectionAsync(string collection, bool force = false, CancellationToken cancellationToken = default);

    /// <summary>Inserts or updates embedded chunks in a collection.</summary>
    Task UpsertChunksAsync(
        string collection,
        IReadOnlyList<EmbeddedChunk> chunks,
        CancellationToken cancellationToken = default);

    /// <summary>Hybrid or dense-only vector search against a single collection.</summary>
    /// <param name="collection">Collection to search.</param>
    /// <param name="denseVector">Dense query embedding.</param>
    /// <param name="sparseVector">Optional sparse query embedding (hybrid).</param>
    /// <param name="topK">Maximum hits to return.</param>
    /// <param name="language">Optional language payload filter.</param>
    /// <param name="minScore">Cosine score floor when not hybrid; ignored for RRF.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<IReadOnlyList<SearchHit>> SearchAsync(
        string collection,
        IReadOnlyList<float> denseVector,
        SparseVector? sparseVector,
        int topK,
        string? language = null,
        float minScore = 0.5f,
        CancellationToken cancellationToken = default);

    /// <summary>Retrieves a chunk payload by id within one collection.</summary>
    Task<ChunkPayload?> GetChunkByIdAsync(
        string collection,
        string chunkId,
        CancellationToken cancellationToken = default);

    /// <summary>Retrieves a chunk by id from one collection or all collections.</summary>
    Task<ChunkPayload?> FindChunkByIdAsync(
        string chunkId,
        string? collection = null,
        CancellationToken cancellationToken = default);

    /// <summary>Scrolls symbol metadata for a single file (outline).</summary>
    Task<IReadOnlyList<FileSymbol>> ScrollFileSymbolsAsync(
        string collection,
        string relPath,
        CancellationToken cancellationToken = default);

    /// <summary>Scrolls lightweight payloads for summary aggregation.</summary>
    Task<IReadOnlyList<PayloadRow>> ScrollAllPayloadsAsync(
        string collection,
        CancellationToken cancellationToken = default);

    /// <summary>Lists all collections with statistics.</summary>
    Task<IReadOnlyList<CollectionStats>> ListCollectionStatsAsync(
        CancellationToken cancellationToken = default);

    /// <summary>Finds chunks matching a symbol name across collections.</summary>
    Task<IReadOnlyList<SearchHit>> FindSymbolInCollectionsAsync(
        string symbolName,
        IReadOnlyList<string> collections,
        int limitPerCollection = 10,
        CancellationToken cancellationToken = default);

    /// <summary>Lists all collection names in the vector store.</summary>
    Task<IReadOnlyList<string>> ListCollectionsAsync(CancellationToken cancellationToken = default);

    /// <summary>Retrieves statistics and configuration for a collection.</summary>
    ValueTask<CollectionStats?> GetCollectionStatsAsync(
        string collection,
        CancellationToken cancellationToken = default);

    /// <summary>Retrieves stored file metadata for change detection.</summary>
    Task<IReadOnlyDictionary<string, FileMetadata>> GetFileMetadataAsync(
        string collection,
        CancellationToken cancellationToken = default);

    /// <summary>Deletes all chunks associated with the given file paths.</summary>
    Task DeleteByPathsAsync(
        string collection,
        IReadOnlyList<string> relPaths,
        CancellationToken cancellationToken = default);

    /// <summary>Marks whether a collection is currently being indexed.</summary>
    Task SetIndexingAsync(
        string collection,
        bool enabled,
        CancellationToken cancellationToken = default);

    /// <summary>Raises when any chunk id is missing from the collection.</summary>
    Task VerifyChunkIdsExistAsync(
        string collection,
        IReadOnlyList<string> chunkIds,
        CancellationToken cancellationToken = default);

    /// <summary>Qdrant Recommend (dense AVERAGE_VECTOR) with optional path_glob post-filter.</summary>
    Task<IReadOnlyList<SearchHit>> RecommendAsync(
        string collection,
        IReadOnlyList<RecommendExample> positive,
        IReadOnlyList<RecommendExample>? negative = null,
        int limit = 5,
        string? language = null,
        string? pathGlob = null,
        CancellationToken cancellationToken = default);

    /// <summary>Find chunks distant from a context centroid (BEST_SCORE negative-only).</summary>
    Task<IReadOnlyList<SearchHit>> FindOutlierChunksAsync(
        string collection,
        IReadOnlyList<string>? contextChunkIds = null,
        int limit = 5,
        string? language = null,
        string? pathGlob = null,
        float? maxSimilarity = null,
        int? maxContextSamples = null,
        CancellationToken cancellationToken = default);

    /// <summary>Scroll chunks whose callees payload matches member or receiver.member.</summary>
    Task<IReadOnlyList<SearchHit>> FindCallersInCollectionsAsync(
        string method,
        IReadOnlyList<string> collections,
        string? receiver = null,
        int limitPerCollection = 10,
        CancellationToken cancellationToken = default);

    /// <summary>Scroll chunk payloads for specific rel_path values.</summary>
    Task<IReadOnlyList<IReadOnlyDictionary<string, string>>> ScrollChunksByPathsAsync(
        string collection,
        IReadOnlyList<string> relPaths,
        IReadOnlyList<string>? payloadFields = null,
        int limit = 500,
        CancellationToken cancellationToken = default);
}
