namespace CodebaseIndexer.Domain.Models;

/// <summary>HTTP_CALLS edge row.</summary>
/// <param name="ChunkId">Calling chunk.</param>
/// <param name="Path">Called endpoint path.</param>
public sealed record GraphHttpCallRow(string ChunkId, string Path);
