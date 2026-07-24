namespace CodebaseIndexer.Domain.Results;

/// <summary>
/// Stable machine-readable error codes for vector-store operations (ADR 0033 Phase 3).
/// Callers should treat unknown codes as opaque.
/// </summary>
public static class StoreErrorCodes
{
    /// <summary>Vector store is unreachable or rejected the request.</summary>
    public const string Unavailable = "store.unavailable";

    /// <summary>Requested collection does not exist.</summary>
    public const string CollectionNotFound = "store.collection_not_found";

    /// <summary>Requested chunk id was not found in the store.</summary>
    public const string ChunkNotFound = "store.chunk_not_found";

    /// <summary>Upsert of embedded chunks failed.</summary>
    public const string Upsert = "store.upsert";

    /// <summary>Search or recommend query against the store failed.</summary>
    public const string Search = "store.search";

    /// <summary>Chunk-id verification failed (missing ids or transport error).</summary>
    public const string VerifyChunks = "store.verify_chunks";

    /// <summary>Collection ensure/create/recreate failed.</summary>
    public const string EnsureCollection = "store.ensure_collection";

    /// <summary>Delete-by-paths failed.</summary>
    public const string Delete = "store.delete";

    /// <summary>Scroll/read of payloads or symbols failed.</summary>
    public const string Scroll = "store.scroll";

    /// <summary>Collection metadata write failed.</summary>
    public const string Metadata = "store.metadata";

    /// <summary>List collections / stats failed.</summary>
    public const string List = "store.list";
}
