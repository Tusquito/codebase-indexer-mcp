namespace CodebaseIndexer.Domain.Models;

/// <summary>Lightweight payload row used for collection summary aggregation.</summary>
/// <param name="RelPath">Repository-relative path.</param>
/// <param name="Language">Source language.</param>
/// <param name="SymbolName">Optional symbol name.</param>
/// <param name="SymbolType">Symbol kind.</param>
/// <param name="StartLine">One-based start line.</param>
/// <param name="EndLine">One-based end line.</param>
public sealed record PayloadRow(
    string RelPath,
    string Language,
    string? SymbolName,
    string SymbolType,
    int StartLine,
    int EndLine);
