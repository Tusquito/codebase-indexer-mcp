namespace CodebaseIndexer.Domain.Models;

/// <summary>Stored metadata for an indexed file used to detect changes.</summary>
/// <param name="Sha256">SHA-256 hash of the file content at last index time.</param>
/// <param name="Mtime">Last modification time as a Unix timestamp, if recorded.</param>
public sealed record FileMetadata(string Sha256, double? Mtime)
{
    /// <summary>SHA-256 hash of the file content at last index time.</summary>
    public string Sha256 { get; init; } = Sha256;

    /// <summary>Last modification time as a Unix timestamp, if recorded.</summary>
    public double? Mtime { get; init; } = Mtime;
}
