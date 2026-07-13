namespace CodebaseIndexer.Domain.Models;

/// <summary>Outcome metrics produced by a completed indexing pipeline run.</summary>
/// <param name="TotalFiles">Total files discovered for indexing.</param>
/// <param name="IndexedFiles">Files successfully indexed in this run.</param>
/// <param name="SkippedFiles">Files skipped because they were unchanged.</param>
/// <param name="TotalChunks">Total chunks written during indexing.</param>
/// <param name="ElapsedSeconds">Seconds elapsed for the pipeline run.</param>
/// <param name="Errors">Per-file or per-step error messages collected during the run.</param>
public sealed record PipelineResult(
    int TotalFiles = 0,
    int IndexedFiles = 0,
    int SkippedFiles = 0,
    int TotalChunks = 0,
    double ElapsedSeconds = 0,
    IReadOnlyList<string> Errors = null!)
{
    /// <summary>Total files discovered for indexing.</summary>
    public int TotalFiles { get; init; } = TotalFiles;

    /// <summary>Files successfully indexed in this run.</summary>
    public int IndexedFiles { get; init; } = IndexedFiles;

    /// <summary>Files skipped because they were unchanged.</summary>
    public int SkippedFiles { get; init; } = SkippedFiles;

    /// <summary>Total chunks written during indexing.</summary>
    public int TotalChunks { get; init; } = TotalChunks;

    /// <summary>Seconds elapsed for the pipeline run.</summary>
    public double ElapsedSeconds { get; init; } = ElapsedSeconds;

    /// <summary>Per-file or per-step error messages collected during the run.</summary>
    public IReadOnlyList<string> Errors { get; init; } = Errors ?? Array.Empty<string>();
}
