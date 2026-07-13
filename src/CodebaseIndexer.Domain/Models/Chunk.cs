namespace CodebaseIndexer.Domain.Models;

/// <summary>A code chunk extracted from a source file.</summary>
/// <param name="Id">Unique identifier of the chunk.</param>
/// <param name="RelPath">Repository-relative path of the source file.</param>
/// <param name="Content">Text content of the chunk.</param>
/// <param name="StartLine">One-based start line in the source file.</param>
/// <param name="EndLine">One-based end line in the source file.</param>
/// <param name="SymbolName">Optional name of the enclosing symbol.</param>
/// <param name="Language">Programming language of the source file.</param>
/// <param name="FileSha256">SHA-256 hash of the parent file content.</param>
public sealed record Chunk(
    ChunkId Id,
    string RelPath,
    string Content,
    int StartLine,
    int EndLine,
    string? SymbolName,
    string Language,
    string FileSha256)
{
    /// <summary>Unique identifier of the chunk.</summary>
    public ChunkId Id { get; init; } = Id;

    /// <summary>Repository-relative path of the source file.</summary>
    public string RelPath { get; init; } = RelPath;

    /// <summary>Text content of the chunk.</summary>
    public string Content { get; init; } = Content;

    /// <summary>One-based start line in the source file.</summary>
    public int StartLine { get; init; } = StartLine;

    /// <summary>One-based end line in the source file.</summary>
    public int EndLine { get; init; } = EndLine;

    /// <summary>Optional name of the enclosing symbol.</summary>
    public string? SymbolName { get; init; } = SymbolName;

    /// <summary>Programming language of the source file.</summary>
    public string Language { get; init; } = Language;

    /// <summary>SHA-256 hash of the parent file content.</summary>
    public string FileSha256 { get; init; } = FileSha256;
}
