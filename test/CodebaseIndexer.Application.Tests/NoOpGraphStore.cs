using CodebaseIndexer.Domain.Models;
using CodebaseIndexer.Domain.Ports;

namespace CodebaseIndexer.Application.Tests;

/// <summary>Configurable no-op <see cref="IGraphStore"/> for unit tests.</summary>
internal sealed class NoOpGraphStore : IGraphStore
{
    public bool Enabled { get; init; }

    /// <summary>When set, <see cref="EnsureSchemaAsync"/> throws this exception.</summary>
    public Exception? EnsureSchemaException { get; init; }

    /// <summary>When set, <see cref="WriteBatchAsync"/> throws this exception.</summary>
    public Exception? WriteBatchException { get; init; }

    public List<string> EnsuredSchemaCalls { get; } = [];

    public List<(string Collection, IReadOnlyList<string> Paths)> DeleteCalls { get; } = [];

    public List<GraphBatch> WrittenBatches { get; } = [];

    public IReadOnlyList<SearchHit> Callers { get; init; } = [];

    public GraphExpansion Expansion { get; init; } = GraphExpansion.Empty;

    public IReadOnlyList<string>? LastExpandChunkIds { get; private set; }

    public int? LastExpandHops { get; private set; }

    public int? LastExpandMaxNodes { get; private set; }

    public string? LastCallerMethod { get; private set; }

    public IReadOnlyList<string>? LastCallerCollections { get; private set; }

    /// <inheritdoc />
    public ValueTask<bool> IsEnabledAsync(CancellationToken cancellationToken = default) =>
        ValueTask.FromResult(Enabled);

    /// <inheritdoc />
    public Task EnsureSchemaAsync(CancellationToken cancellationToken = default)
    {
        EnsuredSchemaCalls.Add("ensure");
        if (EnsureSchemaException is not null)
        {
            throw EnsureSchemaException;
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task DeleteFilesAsync(
        string collection,
        IReadOnlyList<string> relPaths,
        CancellationToken cancellationToken = default)
    {
        DeleteCalls.Add((collection, relPaths));
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<SearchHit>> FindCallersAsync(
        string method,
        IReadOnlyList<string> collections,
        string? receiver = null,
        int limitPerCollection = 10,
        CancellationToken cancellationToken = default)
    {
        LastCallerMethod = method;
        LastCallerCollections = collections;
        return Task.FromResult(Callers);
    }

    /// <inheritdoc />
    public Task<GraphExpansion> ExpandSubgraphAsync(
        IReadOnlyList<string> chunkIds,
        int maxHops,
        int maxNodes,
        CancellationToken cancellationToken = default)
    {
        LastExpandChunkIds = chunkIds;
        LastExpandHops = maxHops;
        LastExpandMaxNodes = maxNodes;
        return Task.FromResult(Expansion);
    }

    /// <inheritdoc />
    public Task WriteBatchAsync(GraphBatch batch, CancellationToken cancellationToken = default)
    {
        WrittenBatches.Add(batch);
        if (WriteBatchException is not null)
        {
            throw WriteBatchException;
        }

        return Task.CompletedTask;
    }
}
