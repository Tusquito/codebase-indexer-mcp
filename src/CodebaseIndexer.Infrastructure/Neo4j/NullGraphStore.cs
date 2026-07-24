using CodebaseIndexer.Domain.Models;
using CodebaseIndexer.Domain.Ports;
using CodebaseIndexer.Domain.Results;

namespace CodebaseIndexer.Infrastructure.Neo4j;

/// <summary>No-op graph store when <c>Graph:Enabled=false</c>.</summary>
public sealed class NullGraphStore : IGraphStore
{
    /// <inheritdoc />
    public ValueTask<bool> IsEnabledAsync(CancellationToken cancellationToken = default) =>
        ValueTask.FromResult(false);

    /// <inheritdoc />
    public Task<Result> EnsureSchemaAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(Result.Success());

    /// <inheritdoc />
    public Task<Result> DeleteFilesAsync(
        string collection,
        IReadOnlyList<string> relPaths,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(Result.Success());

    /// <inheritdoc />
    public Task<Result<IReadOnlyList<SearchHit>>> FindCallersAsync(
        string method,
        IReadOnlyList<string> collections,
        string? receiver = null,
        int limitPerCollection = 10,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(Result<IReadOnlyList<SearchHit>>.Success(Array.Empty<SearchHit>()));

    /// <inheritdoc />
    public Task<Result<GraphExpansion>> ExpandSubgraphAsync(
        IReadOnlyList<string> chunkIds,
        int maxHops,
        int maxNodes,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(Result<GraphExpansion>.Success(GraphExpansion.Empty));

    /// <inheritdoc />
    public Task<Result> WriteBatchAsync(GraphBatch batch, CancellationToken cancellationToken = default) =>
        Task.FromResult(Result.Success());
}
