using CodebaseIndexer.Domain.Models;
using CodebaseIndexer.Domain.Ports;
using CodebaseIndexer.Domain.Results;

namespace CodebaseIndexer.Application.Tests;

/// <summary>Configurable no-op <see cref="IGraphStore"/> for unit tests.</summary>
internal sealed class NoOpGraphStore : IGraphStore
{
    public bool Enabled { get; init; }

    /// <summary>When set, <see cref="EnsureSchemaAsync"/> returns this error.</summary>
    public Error? EnsureSchemaError { get; init; }

    /// <summary>When set, <see cref="WriteBatchAsync"/> returns this error.</summary>
    public Error? WriteBatchError { get; init; }

    /// <summary>Legacy: when set, mapped to <see cref="EnsureSchemaError"/> Dependency failure.</summary>
    public Exception? EnsureSchemaException { get; init; }

    /// <summary>Legacy: when set, mapped to <see cref="WriteBatchError"/> Dependency failure.</summary>
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
    public Task<Result> EnsureSchemaAsync(CancellationToken cancellationToken = default)
    {
        EnsuredSchemaCalls.Add("ensure");
        var error = EnsureSchemaError
            ?? (EnsureSchemaException is null
                ? null
                : new Error(ErrorKind.Dependency, GraphErrorCodes.SchemaInit, EnsureSchemaException.Message));
        return Task.FromResult(error is null ? Result.Success() : Result.Failure(error));
    }

    /// <inheritdoc />
    public Task<Result> DeleteFilesAsync(
        string collection,
        IReadOnlyList<string> relPaths,
        CancellationToken cancellationToken = default)
    {
        DeleteCalls.Add((collection, relPaths));
        return Task.FromResult(Result.Success());
    }

    /// <inheritdoc />
    public Task<Result<IReadOnlyList<SearchHit>>> FindCallersAsync(
        string method,
        IReadOnlyList<string> collections,
        string? receiver = null,
        int limitPerCollection = 10,
        CancellationToken cancellationToken = default)
    {
        LastCallerMethod = method;
        LastCallerCollections = collections;
        return Task.FromResult(Result<IReadOnlyList<SearchHit>>.Success(Callers));
    }

    /// <inheritdoc />
    public Task<Result<GraphExpansion>> ExpandSubgraphAsync(
        IReadOnlyList<string> chunkIds,
        int maxHops,
        int maxNodes,
        CancellationToken cancellationToken = default)
    {
        LastExpandChunkIds = chunkIds;
        LastExpandHops = maxHops;
        LastExpandMaxNodes = maxNodes;
        return Task.FromResult(Result<GraphExpansion>.Success(Expansion));
    }

    /// <inheritdoc />
    public Task<Result> WriteBatchAsync(GraphBatch batch, CancellationToken cancellationToken = default)
    {
        WrittenBatches.Add(batch);
        var error = WriteBatchError
            ?? (WriteBatchException is null
                ? null
                : new Error(ErrorKind.Dependency, GraphErrorCodes.Write, WriteBatchException.Message));
        return Task.FromResult(error is null ? Result.Success() : Result.Failure(error));
    }
}
