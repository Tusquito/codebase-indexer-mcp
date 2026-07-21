namespace CodebaseIndexer.Application.Options;

/// <summary>In-process scheduled reindex (replaces cron sidecar).</summary>
public sealed class ReindexOptions
{
    /// <summary>Configuration section name.</summary>
    public const string SectionName = "Reindex";

    /// <summary>Master switch for the scheduled reindex hosted service.</summary>
    public bool Enabled { get; init; } = true;

    /// <summary>Quartz cron expression (wins over <see cref="Interval"/> when set).</summary>
    public string Cron { get; init; } = "0 0 3 * * ?";

    /// <summary>Optional interval (e.g. <c>6h</c>, <c>30m</c>) when cron is empty.</summary>
    public string Interval { get; init; } = string.Empty;

    /// <summary>Pull git default branch before reindex when safe.</summary>
    public bool GitPull { get; init; } = true;

    /// <summary>Workspace root containing collection folders (empty = Workspace:Path).</summary>
    public string WorkspacePath { get; init; } = string.Empty;

    /// <summary>Per-collection index wait timeout in seconds.</summary>
    public int IndexTimeoutSeconds { get; init; } = 1800;

    /// <summary>Git fetch/pull timeout in seconds.</summary>
    public int GitTimeoutSeconds { get; init; } = 120;
}
