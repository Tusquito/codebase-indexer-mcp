using CodebaseIndexer.Domain.Models;

namespace CodebaseIndexer.Domain.Ports;

/// <summary>Port for persisting and querying embedded code chunks in a vector store.</summary>
public interface IVectorStore
{
    /// <summary>Checks whether a collection exists in the vector store.</summary>
    /// <param name="collection">Name of the collection to check.</param>
    /// <param name="cancellationToken">Token used to cancel the operation.</param>
    /// <returns><see langword="true"/> if the collection exists; otherwise <see langword="false"/>.</returns>
    ValueTask<bool> CollectionExistsAsync(string collection, CancellationToken cancellationToken = default);

    /// <summary>Creates a collection if it does not exist, optionally recreating it when forced.</summary>
    /// <param name="collection">Name of the collection to ensure.</param>
    /// <param name="force">Whether to drop and recreate an existing collection.</param>
    /// <param name="cancellationToken">Token used to cancel the operation.</param>
    /// <returns>A task that completes when the collection is ready.</returns>
    Task EnsureCollectionAsync(string collection, bool force = false, CancellationToken cancellationToken = default);

    /// <summary>Inserts or updates embedded chunks in a collection.</summary>
    /// <param name="collection">Target collection name.</param>
    /// <param name="chunks">Embedded chunks to upsert.</param>
    /// <param name="cancellationToken">Token used to cancel the operation.</param>
    /// <returns>A task that completes when all chunks are stored.</returns>
    Task UpsertChunksAsync(
        string collection,
        IReadOnlyList<EmbeddedChunk> chunks,
        CancellationToken cancellationToken = default);

    /// <summary>Searches a collection for chunks similar to a query string.</summary>
    /// <param name="collection">Collection to search.</param>
    /// <param name="query">Natural-language or code search query.</param>
    /// <param name="limit">Maximum number of hits to return.</param>
    /// <param name="cancellationToken">Token used to cancel the operation.</param>
    /// <returns>Ranked search hits ordered by relevance.</returns>
    Task<IReadOnlyList<SearchHit>> SearchAsync(
        string collection,
        string query,
        int limit,
        CancellationToken cancellationToken = default);

    /// <summary>Lists all collection names in the vector store.</summary>
    /// <param name="cancellationToken">Token used to cancel the operation.</param>
    /// <returns>Collection names known to the store.</returns>
    Task<IReadOnlyList<string>> ListCollectionsAsync(CancellationToken cancellationToken = default);

    /// <summary>Retrieves statistics and configuration for a collection.</summary>
    /// <param name="collection">Name of the collection.</param>
    /// <param name="cancellationToken">Token used to cancel the operation.</param>
    /// <returns>Collection statistics, or <see langword="null"/> if the collection does not exist.</returns>
    ValueTask<CollectionStats?> GetCollectionStatsAsync(
        string collection,
        CancellationToken cancellationToken = default);

    /// <summary>Retrieves stored file metadata for change detection.</summary>
    /// <param name="collection">Name of the collection.</param>
    /// <param name="cancellationToken">Token used to cancel the operation.</param>
    /// <returns>File metadata keyed by repository-relative path.</returns>
    Task<IReadOnlyDictionary<string, FileMetadata>> GetFileMetadataAsync(
        string collection,
        CancellationToken cancellationToken = default);

    /// <summary>Deletes all chunks associated with the given file paths.</summary>
    /// <param name="collection">Name of the collection.</param>
    /// <param name="relPaths">Repository-relative paths whose chunks should be removed.</param>
    /// <param name="cancellationToken">Token used to cancel the operation.</param>
    /// <returns>A task that completes when deletions are applied.</returns>
    Task DeleteByPathsAsync(
        string collection,
        IReadOnlyList<string> relPaths,
        CancellationToken cancellationToken = default);

    /// <summary>Marks whether a collection is currently being indexed.</summary>
    /// <param name="collection">Name of the collection.</param>
    /// <param name="enabled">Whether indexing is in progress for the collection.</param>
    /// <param name="cancellationToken">Token used to cancel the operation.</param>
    /// <returns>A task that completes when the indexing flag is updated.</returns>
    Task SetIndexingAsync(
        string collection,
        bool enabled,
        CancellationToken cancellationToken = default);
}
