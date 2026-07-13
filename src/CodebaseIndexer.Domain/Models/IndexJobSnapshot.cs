namespace CodebaseIndexer.Domain.Models;

/// <summary>Point-in-time snapshot of an indexing job's progress and outcome.</summary>
/// <param name="Collection">Name of the collection being indexed.</param>
/// <param name="Path">Repository-relative path being indexed.</param>
/// <param name="Status">Current lifecycle status of the job.</param>
/// <param name="ElapsedSeconds">Seconds elapsed since the job started.</param>
/// <param name="TotalFiles">Total files discovered for indexing.</param>
/// <param name="IndexedFiles">Files successfully indexed in this run.</param>
/// <param name="SkippedFiles">Files skipped because they were unchanged.</param>
/// <param name="TotalChunks">Total chunks written during indexing.</param>
/// <param name="Errors">Per-file or per-step error messages collected during the run.</param>
/// <param name="ErrorMessage">Top-level failure message when the job terminates abnormally.</param>
public sealed record IndexJobSnapshot(
    string Collection,
    string Path,
    IndexJobStatus Status,
    double ElapsedSeconds,
    int TotalFiles,
    int IndexedFiles,
    int SkippedFiles,
    int TotalChunks,
    IReadOnlyList<string> Errors,
    string ErrorMessage = "")
{
    /// <summary>Name of the collection being indexed.</summary>
    public string Collection { get; init; } = Collection;

    /// <summary>Repository-relative path being indexed.</summary>
    public string Path { get; init; } = Path;

    /// <summary>Current lifecycle status of the job.</summary>
    public IndexJobStatus Status { get; init; } = Status;

    /// <summary>Seconds elapsed since the job started.</summary>
    public double ElapsedSeconds { get; init; } = ElapsedSeconds;

    /// <summary>Total files discovered for indexing.</summary>
    public int TotalFiles { get; init; } = TotalFiles;

    /// <summary>Files successfully indexed in this run.</summary>
    public int IndexedFiles { get; init; } = IndexedFiles;

    /// <summary>Files skipped because they were unchanged.</summary>
    public int SkippedFiles { get; init; } = SkippedFiles;

    /// <summary>Total chunks written during indexing.</summary>
    public int TotalChunks { get; init; } = TotalChunks;

    /// <summary>Per-file or per-step error messages collected during the run.</summary>
    public IReadOnlyList<string> Errors { get; init; } = Errors;

    /// <summary>Top-level failure message when the job terminates abnormally.</summary>
    public string ErrorMessage { get; init; } = ErrorMessage;
}
