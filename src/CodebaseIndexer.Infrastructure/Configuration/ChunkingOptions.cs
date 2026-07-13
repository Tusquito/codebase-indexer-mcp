namespace CodebaseIndexer.Infrastructure.Configuration;

/// <summary>Configuration for code chunking line limits and overlap.</summary>
public sealed class ChunkingOptions
{
    /// <summary>Configuration section name.</summary>
    public const string SectionName = "Chunking";

    /// <summary>Maximum lines per chunk.</summary>
    public int MaxLines { get; init; }

    /// <summary>Number of overlapping lines between adjacent chunks.</summary>
    public int OverlapLines { get; init; }
}
