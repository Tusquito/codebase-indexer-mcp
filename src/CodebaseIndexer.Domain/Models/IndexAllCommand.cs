namespace CodebaseIndexer.Domain.Models;

/// <summary>Parameters for indexing all discovered collections.</summary>
/// <param name="Force">Re-index even when file hashes are unchanged.</param>
/// <param name="Wait">Wait for each job to complete before returning.</param>
/// <param name="TimeoutSeconds">Maximum seconds to wait per job.</param>
public sealed record IndexAllCommand(
    bool Force = false,
    bool Wait = true,
    int TimeoutSeconds = 1800);
