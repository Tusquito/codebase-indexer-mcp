using CodebaseIndexer.Domain.Models;

namespace CodebaseIndexer.Domain.Ports;

/// <summary>Port for optional Neo4j code graph storage and querying (GraphRAG).</summary>
public interface IGraphStore
{
    /// <summary>Checks whether graph I/O is enabled.</summary>
    ValueTask<bool> IsEnabledAsync(CancellationToken cancellationToken = default);

    /// <summary>Creates idempotent constraints and indexes when enabled.</summary>
    Task EnsureSchemaAsync(CancellationToken cancellationToken = default);

    /// <summary>Removes File/Chunk subgraphs for the given paths.</summary>
    Task DeleteFilesAsync(
        string collection,
        IReadOnlyList<string> relPaths,
        CancellationToken cancellationToken = default);

    /// <summary>Finds caller chunks via CALLS.call_token (ADR 0023 Path D).</summary>
    Task<IReadOnlyList<SearchHit>> FindCallersAsync(
        string method,
        IReadOnlyList<string> collections,
        string? receiver = null,
        int limitPerCollection = 10,
        CancellationToken cancellationToken = default);

    /// <summary>Expands a bounded neighborhood around seed chunk ids.</summary>
    Task<GraphExpansion> ExpandSubgraphAsync(
        IReadOnlyList<string> chunkIds,
        int maxHops,
        int maxNodes,
        CancellationToken cancellationToken = default);

    /// <summary>Upserts one index-time graph batch.</summary>
    Task WriteBatchAsync(GraphBatch batch, CancellationToken cancellationToken = default);
}
