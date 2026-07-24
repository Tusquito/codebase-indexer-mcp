using CodebaseIndexer.Domain.Models;
using CodebaseIndexer.Domain.Results;

namespace CodebaseIndexer.Domain.Ports;

/// <summary>Port for persisting and querying embedded code chunks in a vector store.</summary>
public interface IVectorStore
{
    /// <summary>Checks whether a collection exists in the vector store.</summary>
    ValueTask<bool> CollectionExistsAsync(string collection, CancellationToken cancellationToken = default);

    /// <summary>Creates a collection if it does not exist, optionally recreating it when forced.</summary>
    Task<Result> EnsureCollectionAsync(string collection, bool force = false, CancellationToken cancellationToken = default);

    /// <summary>Inserts or updates embedded chunks in a collection.</summary>
    Task<Result> UpsertChunksAsync(
        string collection,
        IReadOnlyList<EmbeddedChunk> chunks,
        bool omitCallees = false,
        IReadOnlyDictionary<string, IReadOnlyList<string>>? graphNodeIdsByChunk = null,
        CancellationToken cancellationToken = default);

    /// <summary>Stamps collection metadata when Neo4j call-site lookup is active.</summary>
    Task<Result> SetCollectionGraphCallSitesAsync(
        string collection,
        bool enabled = true,
        CancellationToken cancellationToken = default);

    /// <summary>Stamps collection metadata when chunks carry graph_node_ids linkage.</summary>
    Task<Result> SetCollectionGraphEnabledAsync(
        string collection,
        bool enabled = true,
        CancellationToken cancellationToken = default);

    /// <summary>Returns true when collection metadata marks Neo4j as call-site engine.</summary>
    ValueTask<bool> CollectionHasGraphCallSitesAsync(
        string collection,
        CancellationToken cancellationToken = default);

    /// <summary>Returns true when collection metadata marks chunks as graph-linked.</summary>
    ValueTask<bool> CollectionHasGraphEnabledAsync(
        string collection,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Hybrid or dense-only vector search against a single collection.
    /// Empty hit lists are success; missing collection yields empty success.
    /// </summary>
    Task<Result<IReadOnlyList<SearchHit>>> SearchAsync(
        string collection,
        IReadOnlyList<float> denseVector,
        SparseVector? sparseVector,
        int topK,
        SourceLanguage? language = null,
        float minScore = 0.5f,
        IReadOnlyList<IReadOnlyList<float>>? colbertVector = null,
        CancellationToken cancellationToken = default);

    /// <summary>Retrieves a chunk payload by id within one collection.</summary>
    Task<Result<ChunkPayload>> GetChunkByIdAsync(
        string collection,
        string chunkId,
        CancellationToken cancellationToken = default);

    /// <summary>Retrieves a chunk by id from one collection or all collections.</summary>
    Task<Result<ChunkPayload>> FindChunkByIdAsync(
        string chunkId,
        string? collection = null,
        CancellationToken cancellationToken = default);

    /// <summary>Scrolls symbol metadata for a single file (outline).</summary>
    Task<Result<IReadOnlyList<FileSymbol>>> ScrollFileSymbolsAsync(
        string collection,
        string relPath,
        CancellationToken cancellationToken = default);

    /// <summary>Scrolls lightweight payloads for summary aggregation.</summary>
    Task<Result<IReadOnlyList<PayloadRow>>> ScrollAllPayloadsAsync(
        string collection,
        CancellationToken cancellationToken = default);

    /// <summary>Lists all collections with statistics.</summary>
    Task<Result<IReadOnlyList<CollectionStats>>> ListCollectionStatsAsync(
        CancellationToken cancellationToken = default);

    /// <summary>Finds chunks matching a symbol name across collections.</summary>
    Task<Result<IReadOnlyList<SearchHit>>> FindSymbolInCollectionsAsync(
        string symbolName,
        IReadOnlyList<string> collections,
        int limitPerCollection = 10,
        CancellationToken cancellationToken = default);

    /// <summary>Lists all collection names in the vector store.</summary>
    Task<Result<IReadOnlyList<string>>> ListCollectionsAsync(CancellationToken cancellationToken = default);

    /// <summary>Retrieves statistics and configuration for a collection.</summary>
    ValueTask<Result<CollectionStats>> GetCollectionStatsAsync(
        string collection,
        CancellationToken cancellationToken = default);

    /// <summary>Retrieves stored file metadata for change detection.</summary>
    Task<Result<IReadOnlyDictionary<string, FileMetadata>>> GetFileMetadataAsync(
        string collection,
        CancellationToken cancellationToken = default);

    /// <summary>Deletes all chunks associated with the given file paths.</summary>
    Task<Result> DeleteByPathsAsync(
        string collection,
        IReadOnlyList<string> relPaths,
        CancellationToken cancellationToken = default);

    /// <summary>Marks whether a collection is currently being indexed.</summary>
    Task<Result> SetIndexingAsync(
        string collection,
        bool enabled,
        CancellationToken cancellationToken = default);

    /// <summary>Fails when any chunk id is missing from the collection.</summary>
    Task<Result> VerifyChunkIdsExistAsync(
        string collection,
        IReadOnlyList<string> chunkIds,
        CancellationToken cancellationToken = default);

    /// <summary>Qdrant Recommend (dense AVERAGE_VECTOR) with optional path_glob post-filter.</summary>
    Task<Result<IReadOnlyList<SearchHit>>> RecommendAsync(
        string collection,
        IReadOnlyList<RecommendExample> positive,
        IReadOnlyList<RecommendExample>? negative = null,
        int limit = 5,
        SourceLanguage? language = null,
        string? pathGlob = null,
        CancellationToken cancellationToken = default);

    /// <summary>Find chunks distant from a context centroid (BEST_SCORE negative-only).</summary>
    Task<Result<IReadOnlyList<SearchHit>>> FindOutlierChunksAsync(
        string collection,
        IReadOnlyList<string>? contextChunkIds = null,
        int limit = 5,
        SourceLanguage? language = null,
        string? pathGlob = null,
        float? maxSimilarity = null,
        int? maxContextSamples = null,
        CancellationToken cancellationToken = default);

    /// <summary>Scroll chunks whose callees payload matches member or receiver.member.</summary>
    Task<Result<IReadOnlyList<SearchHit>>> FindCallersInCollectionsAsync(
        string method,
        IReadOnlyList<string> collections,
        string? receiver = null,
        int limitPerCollection = 10,
        CancellationToken cancellationToken = default);

    /// <summary>Scroll chunk payloads for specific rel_path values.</summary>
    Task<Result<IReadOnlyList<IReadOnlyDictionary<string, string>>>> ScrollChunksByPathsAsync(
        string collection,
        IReadOnlyList<string> relPaths,
        IReadOnlyList<string>? payloadFields = null,
        int limit = 500,
        CancellationToken cancellationToken = default);
}
