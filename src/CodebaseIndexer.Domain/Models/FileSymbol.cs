namespace CodebaseIndexer.Domain.Models;

/// <summary>Symbol metadata for a file outline (no code content).</summary>
/// <param name="ChunkId">Chunk identifier.</param>
/// <param name="SymbolName">Optional symbol name.</param>
/// <param name="SymbolType">Symbol kind.</param>
/// <param name="StartLine">One-based start line.</param>
/// <param name="EndLine">One-based end line.</param>
/// <param name="Language">Source language.</param>
public sealed record FileSymbol(
    string ChunkId,
    string? SymbolName,
    SymbolType SymbolType,
    int StartLine,
    int EndLine,
    SourceLanguage Language);
