namespace CodebaseIndexer.Domain.Results;

/// <summary>
/// Stable machine-readable error codes for index/job orchestration (ADR 0033 Phase 2).
/// Callers should treat unknown codes as opaque.
/// </summary>
public static class IndexErrorCodes
{
    /// <summary>An index job is already queued or running for the collection.</summary>
    public const string JobAlreadyRunning = "job.already_running";

    /// <summary>No tracked index job exists for the collection.</summary>
    public const string JobNotFound = "job.not_found";

    /// <summary>Embedding halted because process memory pressure exceeded the halt threshold.</summary>
    public const string EmbedMemoryPressure = "index.embed.memory_pressure";

    /// <summary>A batch embedding step failed (TEI/ONNX/SDK).</summary>
    public const string EmbedBatch = "index.embed.batch";

    /// <summary>A vector-store upsert step failed.</summary>
    public const string Upsert = "index.upsert";

    /// <summary>Graph schema initialization failed.</summary>
    public const string GraphSchemaInit = "index.graph.schema_init";

    /// <summary>Deleting stale graph files failed.</summary>
    public const string GraphStaleDelete = "index.graph.stale_delete";

    /// <summary>Deleting modified graph files failed.</summary>
    public const string GraphDelete = "index.graph.delete";

    /// <summary>Building a graph write batch failed.</summary>
    public const string GraphBatchBuild = "index.graph.batch_build";

    /// <summary>Writing a graph batch failed.</summary>
    public const string GraphWrite = "index.graph.write";

    /// <summary>Stamping collection graph call-sites metadata failed.</summary>
    public const string GraphCallSitesMetadata = "index.graph.call_sites_metadata";

    /// <summary>Stamping collection graph-enabled metadata failed.</summary>
    public const string GraphEnabledMetadata = "index.graph.enabled_metadata";
}
