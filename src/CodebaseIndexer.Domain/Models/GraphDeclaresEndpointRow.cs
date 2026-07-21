namespace CodebaseIndexer.Domain.Models;

/// <summary>DECLARES_ENDPOINT edge row.</summary>
/// <param name="ChunkId">Declaring chunk.</param>
/// <param name="Path">Endpoint path.</param>
public sealed record GraphDeclaresEndpointRow(string ChunkId, string Path);
