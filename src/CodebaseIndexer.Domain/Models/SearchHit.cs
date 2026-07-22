namespace CodebaseIndexer.Domain.Models;

/// <summary>A single vector search result with chunk metadata and relevance score.</summary>
/// <param name="Id">Unique identifier of the matched chunk.</param>
/// <param name="Score">Relevance score assigned by the vector store.</param>
/// <param name="RelPath">Repository-relative path of the source file.</param>
/// <param name="Language">Programming language of the source file.</param>
/// <param name="StartLine">One-based start line of the chunk in the source file.</param>
/// <param name="EndLine">One-based end line of the chunk in the source file.</param>
/// <param name="SymbolName">Optional name of the enclosing symbol, if detected.</param>
/// <param name="SymbolType">Kind of symbol containing the chunk, such as method or class.</param>
/// <param name="Content">Text content of the matched chunk.</param>
/// <param name="Collection">Name of the collection the hit was retrieved from.</param>
public sealed record SearchHit(
    ChunkId Id,
    double Score,
    string RelPath,
    SourceLanguage Language,
    int StartLine,
    int EndLine,
    string? SymbolName,
    SymbolType SymbolType,
    string Content,
    string Collection = "");
