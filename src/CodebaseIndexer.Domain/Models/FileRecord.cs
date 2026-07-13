namespace CodebaseIndexer.Domain.Models;

/// <summary>A source file discovered during workspace scanning with content and metadata.</summary>
/// <param name="AbsPath">Absolute filesystem path to the file.</param>
/// <param name="RelPath">Repository-relative path of the file.</param>
/// <param name="Language">Detected programming language of the file.</param>
/// <param name="Content">Full text content of the file.</param>
/// <param name="Sha256Hash">SHA-256 hash of the file content.</param>
/// <param name="Mtime">Last modification time as a Unix timestamp, if available.</param>
/// <param name="MtimeSkipped">Whether modification time could not be read.</param>
public sealed record FileRecord(
    string AbsPath,
    string RelPath,
    string Language,
    string Content,
    string Sha256Hash,
    double Mtime = 0,
    bool MtimeSkipped = false)
{
    /// <summary>Absolute filesystem path to the file.</summary>
    public string AbsPath { get; init; } = AbsPath;

    /// <summary>Repository-relative path of the file.</summary>
    public string RelPath { get; init; } = RelPath;

    /// <summary>Detected programming language of the file.</summary>
    public string Language { get; init; } = Language;

    /// <summary>Full text content of the file.</summary>
    public string Content { get; init; } = Content;

    /// <summary>SHA-256 hash of the file content.</summary>
    public string Sha256Hash { get; init; } = Sha256Hash;

    /// <summary>Last modification time as a Unix timestamp, if available.</summary>
    public double Mtime { get; init; } = Mtime;

    /// <summary>Whether modification time could not be read.</summary>
    public bool MtimeSkipped { get; init; } = MtimeSkipped;
}
