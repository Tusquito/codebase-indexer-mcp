namespace CodebaseIndexer.Domain.Models;

/// <summary>Chunk node row for index-time graph upsert.</summary>
/// <param name="ChunkId">Chunk identifier.</param>
/// <param name="RelPath">Owning file path.</param>
/// <param name="StartLine">One-based start line.</param>
/// <param name="EndLine">One-based end line.</param>
public sealed record GraphChunkRow(string ChunkId, string RelPath, int StartLine, int EndLine);
