namespace CodebaseIndexer.Domain.Results;

/// <summary>
/// Stable machine-readable error codes for MCP tool-argument validation (ADR 0033 Phase 3).
/// Callers should treat unknown codes as opaque.
/// </summary>
public static class McpErrorCodes
{
    /// <summary>index_codebase path was missing or root-only.</summary>
    public const string PathRequired = "mcp.path_required";

    /// <summary>Generic tool-argument validation failure.</summary>
    public const string Validation = "mcp.validation";

    /// <summary>stop_indexing targeted a job that is not running.</summary>
    public const string JobNotRunning = "mcp.job_not_running";

    /// <summary>index_all found no collections to re-index.</summary>
    public const string CollectionsEmpty = "mcp.collections_empty";
}
