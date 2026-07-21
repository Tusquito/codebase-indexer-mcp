namespace CodebaseIndexer.Application.Services;

/// <summary>Runs one scheduled git-pull + reindex cycle (no MCP HTTP loopback).</summary>
public interface IScheduledReindexRunner
{
    /// <summary>Executes a single reindex cycle.</summary>
    Task RunOnceAsync(CancellationToken cancellationToken = default);
}
