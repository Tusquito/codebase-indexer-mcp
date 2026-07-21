using CodebaseIndexer.Domain.Models;
using CodebaseIndexer.Domain.Ports;

namespace CodebaseIndexer.Infrastructure.Neo4j;

/// <summary>No-op graph store when <c>Graph:Enabled=false</c>.</summary>
public sealed class NullGraphStore : IGraphStore
{
    /// <inheritdoc />
    public ValueTask<bool> IsEnabledAsync(CancellationToken cancellationToken = default) =>
        ValueTask.FromResult(false);

    /// <inheritdoc />
    public Task EnsureSchemaAsync(CancellationToken cancellationToken = default) =>
        Task.CompletedTask;

    /// <inheritdoc />
    public Task DeleteFilesAsync(
        string collection,
        IReadOnlyList<string> relPaths,
        CancellationToken cancellationToken = default) =>
        Task.CompletedTask;

    /// <inheritdoc />
    public Task<IReadOnlyList<SearchHit>> FindCallersAsync(
        string method,
        IReadOnlyList<string> collections,
        string? receiver = null,
        int limitPerCollection = 10,
        CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<SearchHit>>(Array.Empty<SearchHit>());

    /// <inheritdoc />
    public Task<GraphExpansion> ExpandSubgraphAsync(
        IReadOnlyList<string> chunkIds,
        int maxHops,
        int maxNodes,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(GraphExpansion.Empty);

    /// <inheritdoc />
    public Task WriteBatchAsync(GraphBatch batch, CancellationToken cancellationToken = default) =>
        Task.CompletedTask;
}
