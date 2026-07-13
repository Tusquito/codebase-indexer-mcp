namespace CodebaseIndexer.Application.Options;

/// <summary>Configuration for workspace scanning.</summary>
public sealed class WorkspaceOptions
{
    /// <summary>Configuration section name.</summary>
    public const string SectionName = "Workspace";

    /// <summary>Root directory path of the workspace to index.</summary>
    public string Path { get; init; } = string.Empty;

    /// <summary>Comma-separated directory names excluded from scanning.</summary>
    public string ExcludedDirs { get; init; } = string.Empty;

    /// <summary>Degree of parallelism for file hashing.</summary>
    public int HashWorkerDop { get; init; }

    /// <summary>Readahead buffer size for file enumeration.</summary>
    public int ReadaheadBuffer { get; init; }
}
