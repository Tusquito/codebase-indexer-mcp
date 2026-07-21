namespace CodebaseIndexer.Domain.Models;

/// <summary>Full chunk payload retrieved by chunk id (get_chunk parity).</summary>
/// <param name="ChunkId">Chunk identifier.</param>
/// <param name="RelPath">Repository-relative path.</param>
/// <param name="Content">Full chunk text.</param>
/// <param name="StartLine">One-based start line.</param>
/// <param name="EndLine">One-based end line.</param>
/// <param name="Language">Source language.</param>
/// <param name="FileSha256">Parent file hash.</param>
/// <param name="SymbolName">Optional symbol name.</param>
/// <param name="SymbolType">Symbol kind.</param>
/// <param name="Collection">Collection the chunk was found in (when known).</param>
public sealed record ChunkPayload(
    string ChunkId,
    string RelPath,
    string Content,
    int StartLine,
    int EndLine,
    string Language,
    string FileSha256,
    string? SymbolName,
    string SymbolType,
    string? Collection = null);
