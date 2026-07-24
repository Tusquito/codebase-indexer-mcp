namespace CodebaseIndexer.Domain.Results;

/// <summary>
/// Stable machine-readable error codes for graph-store operations (ADR 0033 Phase 3).
/// Aligns with <see cref="IndexErrorCodes"/> graph codes where applicable.
/// </summary>
public static class GraphErrorCodes
{
    /// <summary>Graph schema initialization failed.</summary>
    public const string SchemaInit = IndexErrorCodes.GraphSchemaInit;

    /// <summary>Deleting stale graph files failed.</summary>
    public const string StaleDelete = IndexErrorCodes.GraphStaleDelete;

    /// <summary>Deleting modified graph files failed.</summary>
    public const string Delete = IndexErrorCodes.GraphDelete;

    /// <summary>Building a graph write batch failed.</summary>
    public const string BatchBuild = IndexErrorCodes.GraphBatchBuild;

    /// <summary>Writing a graph batch failed.</summary>
    public const string Write = IndexErrorCodes.GraphWrite;

    /// <summary>Caller lookup query failed.</summary>
    public const string Query = "graph.query";

    /// <summary>Subgraph expansion query failed.</summary>
    public const string Expand = "graph.expand";

    /// <summary>Graph store is unavailable or rejected the request.</summary>
    public const string Unavailable = "graph.unavailable";
}
