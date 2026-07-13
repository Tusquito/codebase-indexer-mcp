namespace CodebaseIndexer.Domain.Models;

/// <summary>Parameters for starting a single collection index job.</summary>
/// <param name="Collection">Target collection name.</param>
/// <param name="Path">Sub-path within the workspace to index.</param>
/// <param name="Force">Re-index even when file hashes are unchanged.</param>
/// <param name="Wait">Wait for the job to complete before returning.</param>
/// <param name="TimeoutSeconds">Maximum seconds to wait for completion.</param>
public sealed record IndexCodebaseCommand(
    string Collection,
    string Path,
    bool Force = false,
    bool Wait = true,
    int TimeoutSeconds = 1800);
