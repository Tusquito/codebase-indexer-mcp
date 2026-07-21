namespace CodebaseIndexer.Domain.Models;

/// <summary>File node row for index-time graph upsert.</summary>
/// <param name="RelPath">Repository-relative path.</param>
/// <param name="Language">Source language.</param>
/// <param name="Sha256">File content hash.</param>
public sealed record GraphFileRow(string RelPath, string Language, string Sha256);
